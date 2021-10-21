using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Sinotech;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CodeView
{
    [Transaction(TransactionMode.Manual)]
    public class CopyFiles : IExternalCommand
    {
        public static bool trueOrFalse = false; // 視窗選擇確定或取消

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            List<string> sourceFiles = ChooseFiles(); // 選擇來源檔案
            if(trueOrFalse == true)
            {
                Sinotech.ExcelSPPath eSPPath = new ExcelSPPath();
                string targetFolder = eSPPath.ChooseESPPath("CopyFiles"); // 選擇目的資料夾
                int winCheck = eSPPath.dialogResult; // 目的資料夾選擇確認 = 1; 取消 = 2
                if (winCheck == 1)
                {
                    FilesCopy(sourceFiles, targetFolder); // 複製檔案
                }
            }

            return Result.Succeeded;
        }
        // 選擇來源檔案
        private List<string> ChooseFiles()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "選擇檔案";
            ofd.InitialDirectory = ".\\";
            ofd.Filter = "RVT Files (*.rvt)|*.rvt|Word Files (*.docx)|*.docx|All Files (*.*)|*.*";
            ofd.Multiselect = true; // 多選檔案
            List<string> sourceFiles = new List<string>();
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                trueOrFalse = true;
                foreach (string filePath in ofd.FileNames)
                {
                    sourceFiles.Add(filePath);
                }
            }
            else
            {
                trueOrFalse =false;
            }
            return sourceFiles;
        }
        // 複製檔案
        private void FilesCopy(List<string> sourceFiles, string targetFolder)
        {
            // 複製檔案到新位置, 如有相同檔案名稱則覆蓋
            try
            {
                foreach (string sourceFile in sourceFiles)
                {
                    if (System.IO.Directory.Exists(targetFolder))
                    {
                        string fileName = System.IO.Path.GetFileName(sourceFile);
                        System.IO.File.Copy(sourceFile, targetFolder + "\\" + fileName, true);
                    }
                    else
                    {
                        Directory.CreateDirectory(targetFolder); //新增資料夾
                        string fileName = System.IO.Path.GetFileName(sourceFile);
                        System.IO.File.Copy(sourceFile, targetFolder + "\\" + fileName, true);
                    }
                }
                TaskDialog.Show("Revit", "完成");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Revit", ex.Message);
            }
        }
    }
}
