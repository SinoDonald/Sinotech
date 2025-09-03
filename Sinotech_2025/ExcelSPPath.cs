using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Autodesk.Revit.UI;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech_2025
{
    public class ExcelSPPath
    {
        // 選取視窗結果
        public int dialogResult = 2;
        public string excelPath = ""; // Excel路徑
        public string spPath = ""; // 共用參數路徑
        public string useItemsFold = ""; // 常用元件資料夾
        public string copyFiles = ""; // 複製檔案

        // 找到Excel、共用參數與常用元件資料夾檔案路徑
        public void ReadPath()
        {
            LicPath licPath = new LicPath();
            string pathString = licPath.pathStr;

            if (!System.IO.File.Exists(pathString))
            {
                using (System.IO.FileStream fs = System.IO.File.Create(pathString))
                {

                }
            }
            //excelPath = System.IO.File.ReadAllText(pathString);

            List<string[]> tmpLine = new List<string[]>();
            try
            {
                string line = string.Empty;
                using (StreamReader sr = new StreamReader(pathString))
                {
                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] keyValue = line.Split('>');
                        tmpLine.Add(keyValue);
                    }
                    sr.Close();
                }
            }
            catch (Exception)
            {

            }
            for(int i = 0; i < tmpLine.Count(); i++)
            {
                try
                {
                    if (tmpLine[i][0].Equals("Excel"))
                    {
                        excelPath = tmpLine[i][1];
                    }
                    else if (tmpLine[i][0].Equals("SharedParameter"))
                    {
                        spPath = tmpLine[i][1];
                    }
                    else if (tmpLine[i][0].Equals("UseItems"))
                    {
                        useItemsFold = tmpLine[i][1];
                    }
                    else if (tmpLine[i][0].Equals("CopyFiles"))
                    {
                        copyFiles = tmpLine[i][1];
                    }
                }
                catch (IndexOutOfRangeException)
                {

                }
            }
        }
        // 選擇Excel或共用參數檔案路徑
        public string ChooseESPPath(string eSP)
        {
            LicPath licPath = new LicPath();
            string pathString = licPath.pathStr;
            string path = string.Empty;
            ReadPath();

            if (!System.IO.File.Exists(pathString))
            {
                using (System.IO.FileStream fs = System.IO.File.Create(pathString))
                {

                }
            }

            try
            {
                OpenFileDialog ofd = new OpenFileDialog();
                if (string.IsNullOrEmpty(ofd.InitialDirectory))
                {
                    // 桌面
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    // 預設路徑
                    try
                    {
                        int lastCount = 0;
                        if (eSP.Equals("Excel"))
                        {
                            lastCount = excelPath.LastIndexOf('\\');
                            licPath.previous = excelPath.Substring(0, lastCount) + "\\";
                        }                        
                        else if(eSP.Equals("SP"))
                        {
                            lastCount = spPath.LastIndexOf('\\');
                            licPath.previous = spPath.Substring(0, lastCount) + "\\";
                        }
                        else if (eSP.Equals("UseItems"))
                        {
                            licPath.previous = useItemsFold;
                        }
                        else if (eSP.Equals("CopyFiles"))
                        {
                            licPath.previous = copyFiles;
                        }
                        ofd.InitialDirectory = licPath.previous;
                    }
                    catch
                    {
                        ofd.InitialDirectory = desktop;
                    }
                }
                // 選擇Excel檔案
                if (eSP.Equals("Excel"))
                {
                    ofd.Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*";
                    ofd.Title = "請開啟文字檔案";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        dialogResult = 1;
                        excelPath = ofd.FileName;
                    }
                    path = excelPath;
                }
                else if (eSP.Equals("SP"))
                {
                    ofd.Filter = "文字檔 (*.txt)|*.txt|All Files (*.*)|*.*";
                    ofd.Title = "請開啟文字檔案";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        dialogResult = 1;
                        spPath = ofd.FileName;
                    }
                    path = spPath;
                }
                else if (eSP.Equals("UseItems"))
                {
                    ofd.Filter = "Rfa Files (*.rfa)|*.rfa|All Files (*.*)|*.*";
                    ofd.Title = "請開啟rfa檔案";
                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        dialogResult = 1;
                        useItemsFold = ofd.FileName;
                    }
                    path = useItemsFold;
                }
                else if (eSP.Equals("CopyFiles"))
                {
                    FolderBrowserDialog dilog = new FolderBrowserDialog();
                    dilog.SelectedPath = licPath.previous; // 預設路徑
                    dilog.Description = "請選擇資料夾";
                    if (dilog.ShowDialog() == DialogResult.OK)
                    {
                        dialogResult = 1;
                        copyFiles = dilog.SelectedPath;
                    }
                    path = copyFiles;
                }
                if (dialogResult == 1) // 有變更路徑在填寫tmp.txt
                {
                    string writePath = "Excel>" + excelPath + "\r\nSharedParameter>" + spPath + "\r\nUseItems>" + useItemsFold + "\r\nCopyFiles>" + copyFiles;
                    if (writePath != null && writePath != "")
                    {
                        System.IO.File.WriteAllText(pathString, writePath, Encoding.UTF8);
                    }
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit", ex.ToString());
            }

            return path;
        }
    }
}
