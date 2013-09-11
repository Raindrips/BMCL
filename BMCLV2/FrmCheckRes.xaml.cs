﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Net;
using System.IO;
using System.Xml;
using System.Data;
using System.Threading;

using BMCLV2.Lang;

namespace BMCLV2
{
    /// <summary>
    /// FrmCheckRes.xaml 的交互逻辑
    /// </summary>
    public partial class FrmCheckRes : Window
    {
        
        public FrmCheckRes()
        {
            InitializeComponent();
        }
        string URL_RESOURCE_BASE = FrmMain.URL_RESOURCE_BASE;
        DataTable dt=new DataTable();
        int InDownloading = 0;
        int WaitingForSync = 0;
        bool ischecked = false;
        int checkedfile = 0;
        private void frmCheckRes_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                dt.Columns.Add("FileName");
                dt.Columns.Add("ModifyTime");
                dt.Columns.Add("Size");
                dt.Columns.Add("Status");
                dt.Columns.Add("MD5");
                byte[] buffer = (new WebClient()).DownloadData(URL_RESOURCE_BASE);
                Stream RawXml = new MemoryStream(buffer);
                XmlDocument doc = new XmlDocument();
                doc.Load(RawXml);
                XmlNodeList nodeLst = doc.GetElementsByTagName("Contents");
                for (int i = 0; i < nodeLst.Count; i++)
                {
                    XmlNode node = nodeLst.Item(i);
                    if (node.GetType() == null)
                        continue;
                    XmlElement element = (XmlElement)node;
                    String key = element.GetElementsByTagName("Key").Item(0).ChildNodes.Item(0).Value;
                    String modtime = element.GetElementsByTagName("LastModified").Item(0).ChildNodes.Item(0).Value;
                    String etag = element.GetElementsByTagName("ETag") == null ? "-" : element.GetElementsByTagName("ETag").Item(0).ChildNodes.Item(0).Value;
                    long size = long.Parse(element.GetElementsByTagName("Size").Item(0).ChildNodes.Item(0).Value);
                    if (size <= 0L)
                        continue;
                    dt.Rows.Add(new string[] { key, modtime, size.ToString(), LangManager.GetLangFromResource("ResWaitingForCheck"), etag.Replace("\"", "").Trim() });
                    listRes.DataContext = dt;
                }
            }
            catch (WebException ex)
            {
                MessageBox.Show(LangManager.GetLangFromResource("ResServerTimeOut") + ex.Message);
                Logger.Log("与资源服务器通信出错");
                Logger.Log(ex);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LangManager.GetLangFromResource("ResServerTimeOut") + ex.Message);
                Logger.Log("与资源服务器通信出错");
                Logger.Log(ex);
                this.Close();
            }
        }

        private void btnCheck_Click(object sender, RoutedEventArgs e)
        {
            ischecked = true;
            prs.Maximum = listRes.Items.Count;
            prs.Value = 0;
            checkedfile = 0;
            foreach (object item in listRes.Items)
            {
                checkedfile++;
                ThreadPool.QueueUserWorkItem(new WaitCallback(GetMD5HashFromFile), checkedfile);
                //GetMD5HashFromFile(prs.Value);
            }
            Thread thCount=new Thread(new ThreadStart(new System.Windows.Forms.MethodInvoker(delegate
            {
                while (checkedfile != dt.Rows.Count) { }
                Logger.Log(string.Format("检查资源文件，共有{0}个文件待同步，共计{1}个文件", WaitingForSync, dt.Rows.Count));
            })));
            thCount.Start();
        }
        public void GetMD5HashFromFile(object obj)
        {
            int num = (int)obj - 1;
            string fileName = Environment.CurrentDirectory + @"\.minecraft\assets\" + dt.Rows[num]["FileName"].ToString();
            try
            {
                FileStream file = new FileStream(fileName, FileMode.Open);
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] retVal = md5.ComputeHash(file);
                file.Close();
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < retVal.Length; i++)
                {
                    sb.Append(retVal[i].ToString("x2"));
                }
                string lmd5 = sb.ToString();
                if (lmd5.Trim() == dt.Rows[num]["MD5"].ToString())
                {
                    lock (dt)
                    {
                        dt.Rows[num]["Status"] = LangManager.GetLangFromResource("ResNoNeedForSync");
                        Logger.Log(string.Format("检查资源文件{0}，无需同步", dt.Rows[num]["文件名"]));
                    }
                }
                else
                {
                    lock (dt)
                    {
                        dt.Rows[num]["Status"] = LangManager.GetLangFromResource("ResWaitingForSync");
                        Logger.Log(string.Format("检查资源文件{0}，需要同步", dt.Rows[num]["文件名"]));
                    }
                    WaitingForSync++;
                }
            }
            catch (Exception)
            {
                lock (dt)
                {
                    dt.Rows[num]["Status"] = LangManager.GetLangFromResource("ResWaitingForSync");
                }
                WaitingForSync++;
            }
            Dispatcher.Invoke(new System.Windows.Forms.MethodInvoker(delegate { prs.Value++; }));
        }

        private void btnSync_Click(object sender, RoutedEventArgs e)
        {
            if (WaitingForSync == 0)
            {
                MessageBox.Show(LangManager.GetLangFromResource("ResNoFileForSync"));
            }
            if (!ischecked)
                btnCheck_Click(null, null);
            prs.Maximum = WaitingForSync;
            prs.Value = 0;
            int num = -1;
            foreach (object item in listRes.Items)
            {
                num++;
                if (dt.Rows[num]["Status"].ToString() == LangManager.GetLangFromResource("ResWaitingForSync"))
                {
                    WebClient downer = new WebClient();
                    StringBuilder rpath = new StringBuilder(FrmMain.URL_RESOURCE_BASE);
                    StringBuilder lpath = new StringBuilder(Environment.CurrentDirectory + @"\.minecraft\assets\");
                    rpath.Append(dt.Rows[num]["FileName"].ToString());
                    lpath.Append(dt.Rows[num]["FileName"].ToString());
                    if (!Directory.Exists(System.IO.Path.GetDirectoryName(lpath.ToString())))
                    {
                        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(lpath.ToString()));
                    }
                    downer.DownloadFileCompleted += downer_DownloadFileCompleted;
                    InDownloading++;
                    ThreadPool.QueueUserWorkItem(a => Downer(new Uri(rpath.ToString()), lpath.ToString(), num, downer));
                }
            }
        }
        void Downer(Uri url, string lpath, int num,WebClient downer)
        {
            Logger.Log(string.Format("下载资源文件{0}", url.ToString()));
            downer.DownloadFileAsync(url, lpath.ToString(), num);
        }
        void downer_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            InDownloading--;
            int num = (int)e.UserState;
            Logger.Log(string.Format("下载资源文件{0}", dt.Rows[num]["FileName"]));
            lock (dt)
            {
                dt.Rows[num]["Status"] = LangManager.GetLangFromResource("ResInSync");
            }
            Dispatcher.Invoke(new System.Windows.Forms.MethodInvoker(delegate { prs.Value++; }));
            if (InDownloading == 0)
            {
                MessageBox.Show(LangManager.GetLangFromResource("ResFinish"));
                Dispatcher.Invoke(new System.Windows.Forms.MethodInvoker(delegate { this.Close(); }));
            }
        }
    }
}
