using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SpeedTools
{
    [Transaction(TransactionMode.Manual)]
    public class AutoUpdate : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;
            uiapp.Application.FailuresProcessing += FaliureProcessor; // 錯誤訊息處理方式 (關閉警示視窗)
            try
            {
                ChooseFolder(commandData); // 選擇資料夾, 並找到所有子資料夾
            }
            catch (Exception)
            {

            }
            uiapp.Application.FailuresProcessing += FaliureProcessor;
            return Result.Succeeded;
        }
        // 選擇資料夾, 並找到所有子資料夾
        private void ChooseFolder(ExternalCommandData commandData)
        {
            // 選擇要轉檔的資料夾
            FolderBrowserDialog chooseFolder = new FolderBrowserDialog();
            chooseFolder.Description = "請選擇要升級檔案的資料夾";
            if (DialogResult.OK == chooseFolder.ShowDialog())
            {
                // 選擇另存新檔的資料夾
                FolderBrowserDialog saveAsFolder = new FolderBrowserDialog();
                saveAsFolder.Description = "請選擇另存新檔的資料夾";
                if(DialogResult.OK == saveAsFolder.ShowDialog())
                {
                    string saveAsPath = saveAsFolder.SelectedPath;
                    // 選擇資料夾, 並找到所有子資料夾
                    string selectedPath = chooseFolder.SelectedPath;
                    string[] directorys = Directory.GetDirectories(@selectedPath, "*", SearchOption.AllDirectories);
                    // 將所有資料夾路徑存到paths中
                    IList<string> paths = new List<string>();
                    paths.Add(selectedPath);
                    foreach (string directory in directorys)
                    {
                        paths.Add(directory);
                    }
                    // 升級失敗rvt
                    string failedName = string.Empty;
                    int count = 0;

                    // 取得資料夾內所有檔案, 如副檔名為.rvt則開啟
                    foreach (string path in paths)
                    {
                        DirectoryInfo di = new DirectoryInfo(path);
                        foreach (var fileName in di.GetFiles())
                        {
                            string[] fileNameArray = fileName.ToString().Split(new char[] { '.' });
                            if (fileNameArray.Length == 2) // 原始檔, 自動備份的不編輯
                            {
                                if (Path.GetExtension(path + "\\" + fileName).Equals(".rvt"))
                                {
                                    // 找到當前文件doc
                                    UIApplication uiapp = commandData.Application;
                                    Document doc = commandData.Application.ActiveUIDocument.Document;
                                    //uiapp.DialogBoxShowing += DialogBoxShowing; // 彈跳的對話視窗
                                    // 讀取下個要開啟的rvt路徑
                                    string newFilePath = @path + "\\" + fileName;
                                    try
                                    {
                                        UIDocument newUIDoc = uiapp.OpenAndActivateDocument(newFilePath);
                                        // 關閉當前的doc
                                        doc.Close(false);
                                        //// 儲存
                                        //newUIDoc.Document.Save();
                                        // 另存新檔
                                        string docName = Path.GetFileName(newUIDoc.Document.PathName); // 檔名
                                        SaveAsOptions opt = new SaveAsOptions();
                                        opt.OverwriteExistingFile = false; // 不要覆寫
                                        newUIDoc.Document.SaveAs(saveAsPath + "\\" + docName, opt);
                                    }
                                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                                    {
                                        count++;
                                        failedName += "\n" + fileName + ", 未正常開啟與關閉.";
                                    }
                                    catch (Autodesk.Revit.Exceptions.CorruptModelException) // rvt使用更高版次Revit
                                    {
                                        count++;
                                        failedName += "\n" + fileName + ", 已使用更高版次開啟.";
                                    }
                                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                                    {
                                        count++;
                                        failedName += "\n" + fileName + ", 未正常開啟與關閉.";
                                    }
                                    catch (Exception)
                                    {
                                        count++;
                                        failedName += "\n" + fileName + ", 開啟Revit檔案失敗.";
                                    }
                                }
                            }
                        }
                    }

                    if (count > 0)
                    {
                        TaskDialog.Show("Revit", "升版完成.\n\n" + count + "個檔案失敗：" + failedName);
                    }
                    else
                    {
                        TaskDialog.Show("Revit", "升版完成.\n" + count + "個檔案失敗.");
                    }
                }
            }
        }
        // 錯誤訊息處理方式 (關閉警示視窗)
        private void FaliureProcessor(object sender, FailuresProcessingEventArgs e)
        {
            bool hasFailure = false;
            FailuresAccessor fas = e.GetFailuresAccessor();
            List<FailureMessageAccessor> fma = fas.GetFailureMessages().ToList();
            List<ElementId> ElemntsToDelete = new List<ElementId>();
            fas.DeleteAllWarnings();

            foreach (FailureMessageAccessor fa in fma)
            {
                try
                {
                    // 使用以下刪除警告元素
                    List<ElementId> FailingElementIds = fa.GetFailingElementIds().ToList();
                    ElementId FailingElementId = FailingElementIds[0];
                    if (!ElemntsToDelete.Contains(FailingElementId))
                    {
                        ElemntsToDelete.Add(FailingElementId);
                    }
                    hasFailure = true;
                    fas.DeleteWarning(fa);
                }
                catch (Exception)
                {

                }
            }
            if (ElemntsToDelete.Count > 0)
            {
                fas.DeleteElements(ElemntsToDelete);
            }
            // 在外部命令結束後，使用以下行禁用消息抑制器：CachedUiApp.Application.FailuresProcessing -= FaliureProcessor;
            if (hasFailure)
            {
                e.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
            }
            e.SetProcessingResult(FailureProcessingResult.Continue);
        }
    }
}
