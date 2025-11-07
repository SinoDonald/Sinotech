using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using View = Autodesk.Revit.DB.View;

namespace Sinotech_2020.Plotting
{
    public partial class ChooseView : System.Windows.Forms.Form
    {
        public class ViewInfo
        {
            public View view = null; // 視圖
            public string vftName = string.Empty; // 圖框
            public string name = string.Empty; // 名稱
            public int levelId = 0; // LevelId
            public string picNumber = string.Empty; // 電腦圖號
        }
        public class FormatOption
        {
            public string format { get; set; } // 格式類型
            public List<string> options = new List<string>(); // 格式類型選項
        }
        UIApplication formUIApp = null;
        Autodesk.Revit.ApplicationServices.Application formApp = null;
        Document formDoc = null;
        List<ViewInfo> viewInfoList = new List<ViewInfo>(); // 儲存所有的View
        List<ViewInfo> chooseViewSheets = new List<ViewInfo>(); // 選擇要匯出的圖紙
        List<FormatOption> formatOptionList = new List<FormatOption>(); // 格式類型與選項
        List<string> checkedNodesList = new List<string>(); // 儲存選取的視圖節點
        bool trueOrFlase = false; // 有無選擇匯出路徑
        public ChooseView(UIApplication uiapp, Autodesk.Revit.ApplicationServices.Application app, Document doc)
        {
            InitializeComponent();
            this.formUIApp = uiapp;
            this.formApp = app;
            this.formDoc = doc;

            FormatsOption(doc); // 查詢專案中DWG、DGN、PDF所擁有的選項

            // ComboBox加入要匯出的設定選項, 預設為DWG
            foreach (FormatOption formatOption in formatOptionList)
            {
                formatCB.Items.Add(formatOption.format);
            }
            formatCB.Text = formatCB.Items[0].ToString(); // DWG

            viewInfoList = new List<ViewInfo>(); // 將View清空
            viewInfoList = AllViews(doc); // 找到專案中所有視圖
            CreateNodes(viewInfoList); // 新增節點
            treeView1.ExpandAll(); // 全部展開

            CenterToScreen(); // 置中
        }
        // 查詢專案中DWG、DGN、PDF所擁有的選項
        private void FormatsOption(Document doc)
        {
            formatOptionList = new List<FormatOption>(); // 清空格式類型與選項
            string[] formats = new string[] { "DWG", "DGN", "PDF" };
            foreach(string format in formats)
            {
                FormatOption formatOption = new FormatOption();
                formatOption.format = format; // 格式類型
                if (format.Equals("DWG"))
                {
                    formatOption.options = DWGExportOptions.GetPredefinedSetupNames(doc).ToList();
                }
                else if (format.Equals("DGN"))
                {
                    formatOption.options = DGNExportOptions.GetPredefinedSetupNames(doc).ToList();
                }
                else if (format.Equals("PDF"))
                {
                    ICollection<PrintSetting> printSettings = new FilteredElementCollector(doc).OfClass(typeof(PrintSetting)).Cast<PrintSetting>().ToList();
                    foreach(PrintSetting printSetting in printSettings)
                    {
                        formatOption.options.Add(printSetting.Name);
                    }
                }
                formatOptionList.Add(formatOption);
            }

        }
        // 找到專案中所有視圖
        private List<ViewInfo> AllViews(Document doc)
        {
            List<ViewInfo> viewInfoList = new List<ViewInfo>();
            // 找到所有的View
            List<View> views = new FilteredElementCollector(doc).OfClass(typeof(View)).WhereElementIsNotElementType().Cast<View>().ToList();
            int count = 1;
            foreach (View view in views)
            {
                ViewInfo viewInfo = new ViewInfo();
                if (!view.IsTemplate && view != null) // 視圖專案中有開啟使用且不為null
                {
                    string[] viewTitle = view.Title.Split(':');
                    try
                    {
                        viewInfo.view = view;
                        viewInfo.vftName = viewTitle[0].Trim();
                        viewInfo.name = viewTitle[1].Trim();
                        if(view is ViewSheet)
                        {
                            // 電腦圖號
                            try
                            {
                                string picNumber = string.Empty;
                                try { picNumber = view.LookupParameter("圖框-電腦圖號").AsString(); } catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

                                if (picNumber != null)
                                {
                                    viewInfo.picNumber = picNumber;
                                }
                                else
                                {
                                    viewInfo.picNumber = "NoNumber_" + count;
                                    count++;
                                    //viewInfo.picNumber = view.get_Parameter(BuiltInParameter.VIEWER_SHEET_NUMBER).AsString();
                                }
                            }
                            catch (Exception)
                            {
                                viewInfo.picNumber = "NoNumber_" + count;
                                count++;
                            }
                            if (view.GenLevel != null)
                            {
                                viewInfo.levelId = (int)view.GenLevel.Id.IntegerValue;
                            }
                            if (view.CanBePrinted == true)
                            {
                                viewInfoList.Add(viewInfo);
                            }
                        }
                    }
                    catch (System.IndexOutOfRangeException)
                    {

                    }
                }
            }

            string viewString = string.Empty;
            List<string> vftNames = viewInfoList.Select(x => x.vftName).Distinct().OrderBy(x => x).ToList();
            foreach (string vftName in vftNames)
            {
                viewString += vftName + "\n";
                // 各個ViewFamilyType的樓層名稱, 依照LevelId排序
                List<ViewInfo> viewInfos = viewInfoList.Where(x => x.vftName.Equals(vftName)).OrderBy(x => x.vftName).ThenBy(x => x.levelId).ToList();
                {
                    foreach (ViewInfo viewInfo in viewInfos)
                    {
                        viewString += viewInfo.name + "\n";
                    }
                }
                viewString += "\n";
            }
            return viewInfoList;
        }
        // 新增節點
        private void CreateNodes(List<ViewInfo> viewInfoList)
        {
            // 找到"圖紙"或"Sheet"的視圖
            string vftName = viewInfoList.Where(x => x.vftName.Equals("圖紙") || x.vftName.Equals("Sheet")).Select(x => x.vftName).Distinct().OrderBy(x => x).FirstOrDefault();
            treeView1.Nodes.Add(vftName);
            treeView1.Nodes[0].Checked = true; // 圖紙預設勾選

            // 依圖紙與名稱排序
            List<ViewInfo> viewInfos = viewInfoList.Where(x => x.vftName.Equals(vftName)).Distinct().OrderBy(x => x.vftName).ThenBy(x => x.name).ToList();
            int nodeCount = 0;
            foreach (ViewInfo viewInfo in viewInfos)
            {
                treeView1.Nodes[0].Nodes.Add(viewInfo.name);
                // 預設勾選
                treeView1.Nodes[0].Nodes[nodeCount].Checked = true;
                nodeCount++;
            }
        }
        // 全選
        private void treeView1_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // 檢查狀態變更時, 才會執行
            if (e.Action != TreeViewAction.Unknown)
            {
                if (e.Node.Nodes.Count > 0)
                {
                    // 傳入檢查狀態已變更的TreeNode當前Checked值
                    this.CheckAllChildNodes(e.Node, e.Node.Checked);
                }
            }
            // 儲存選取Element的名稱與ID
            if (e.Node.Checked)
            {
                if (e.Node.Level.Equals(1))
                {
                    // 儲存上層與被選取的節點名稱
                    checkedNodesList.Add(e.Node.Parent.Text + ":" + e.Node.Text);
                }
            }
            else
            {
                if (e.Node.Level.Equals(1))
                {
                    checkedNodesList.Remove(e.Node.Parent.Text + ":" + e.Node.Text);
                }
            }
        }
        // 檢查子節點做全選
        private void CheckAllChildNodes(TreeNode treeNode, bool nodeChecked)
        {
            foreach (TreeNode node in treeNode.Nodes)
            {
                node.Checked = nodeChecked;
                if (node.Nodes.Count > 0)
                {
                    // 如果當前節點有子節點, 則遞迴使用CheckAllChildNodes
                    this.CheckAllChildNodes(node, nodeChecked);
                }
            }
        }
        // 確定
        private void sure_Click(object sender, EventArgs e)
        {
            chooseViewSheets = new List<ViewInfo>(); // 清除選擇要匯出的圖紙
            trueOrFlase = false; // 預設未選擇匯出路徑

            foreach (string checkedNode in checkedNodesList)
            {
                string nodeName = checkedNode.Replace("圖紙:", "").Replace("Sheet:", "");
                ViewInfo viewInfo = (from x in viewInfoList
                                     where x.name.Equals(nodeName)
                                     select x).FirstOrDefault();
                chooseViewSheets.Add(viewInfo);
            }
            // 匯出視圖成DWG檔
            ExportViewPlan(formDoc, chooseViewSheets);
        }
        // 匯出視圖成DWG檔
        public void ExportViewPlan(Document doc, List<ViewInfo> chooseViewSheets)
        {
            try
            {
                // 取得當前時間
                DateTime timeStart = DateTime.Now;
                string dt = string.Format("{0:yyyyMMdd_HHmm}", timeStart);
                // 查詢使用的Revit版本
                string versionNumber = formApp.VersionNumber;
                // 選擇匯出路徑, 預設為桌面+匯出DWG+日期時間
                string path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                path = ChooseExportPath(path) + "\\匯出_" + dt;

                // 有選擇路徑的情況下, 則進行檔案匯出
                if (trueOrFlase == true)
                {
                    if (Directory.Exists(path) == false)
                    {
                        Directory.CreateDirectory(path); // 創建資料夾
                    }

                    // 將選擇要匯出的圖紙加入id中
                    foreach (ViewInfo viewInfo in chooseViewSheets)
                    {
                        // 先確認此視圖是否可Printed
                        if (viewInfo.view.CanBePrinted == true)
                        {
                            try
                            {
                                // 開啟View
                                formUIApp.ActiveUIDocument.ActiveView = viewInfo.view;
                                // 關閉其他視圖
                                View currView = formDoc.ActiveView;
                                formUIApp.ActiveUIDocument.RequestViewChange(currView);
                                IList<UIView> openViews = formUIApp.ActiveUIDocument.GetOpenUIViews();
                                foreach (UIView openView in openViews)
                                {
                                    if (openView.ViewId != currView.Id)
                                    {
                                        openView.Close();
                                    }
                                }
                                // 執行交易
                                using (Transaction trans = new Transaction(doc, "匯出視圖"))
                                {
                                    // 開始交易
                                    trans.Start();
                                    ICollection<ElementId> viewSheetElementIds = new List<ElementId>();
                                    viewSheetElementIds.Add(viewInfo.view.Id);
                                    // 確認要匯出的格式
                                    if (formatCB.Text.Equals("DWG"))
                                    {
                                        // 選擇的設置為何
                                        DWGExportOptions dwgOptions = new DWGExportOptions();
                                        if (optionCB.Text != "")
                                        {
                                            dwgOptions = DWGExportOptions.GetPredefinedOptions(doc, optionCB.Text);
                                        }
                                        List<View> views = new FilteredElementCollector(doc).OfClass(typeof(View)).WhereElementIsNotElementType().Cast<View>().ToList();
                                        View addView = (from x in views
                                                        where x.Id.ToString().Equals(viewInfo.view.Id.ToString())
                                                        select x).FirstOrDefault();
                                        viewSheetElementIds = new List<ElementId>();
                                        viewSheetElementIds.Add(addView.Id);
                                        // 匯出, 檔名為電腦圖號
                                        doc.Export(path, viewInfo.picNumber, viewSheetElementIds, dwgOptions);
                                        GC.Collect();
                                        GC.WaitForPendingFinalizers();
                                    }
                                    else if (formatCB.Text.Equals("DGN"))
                                    {
                                        // 創建 DGN export options
                                        DGNExportOptions dgnOptions = new DGNExportOptions();
                                        // 選擇的設置為何
                                        if (optionCB.Text != "")
                                        {
                                            dgnOptions = DGNExportOptions.GetPredefinedOptions(doc, optionCB.Text);
                                        }
                                        else
                                        {
                                            dgnOptions.HatchPatternsFileName = @"C:\Program Files\Autodesk\Revit " + versionNumber + @"\ACADInterop\acdbiso.pat";
                                            dgnOptions.SeedName = @"C:\Program Files\Autodesk\Revit " + versionNumber + @"\ACADInterop\V8-Metric-Seed3D.dgn";
                                            dgnOptions.LayerMapping = "AIA";
                                        }
                                        // 匯出, 檔名為電腦圖號
                                        doc.Export(path, viewInfo.picNumber, viewSheetElementIds, dgnOptions);
                                        GC.Collect();
                                        GC.WaitForPendingFinalizers();
                                    }
                                    else if (formatCB.Text.Equals("PDF"))
                                    {
                                        try
                                        {
                                            //// 建立PDF匯出選項
                                            //PDFExportOptions options = new PDFExportOptions();
                                            //string fileName = viewInfo.picNumber; // 檔名為「圖框-電腦圖號」
                                            //options.FileName = fileName; // 直接指定檔名
                                            //options.ColorDepth = ColorDepthType.BlackLine; // 色彩深度
                                            //options.ExportQuality = PDFExportQualityType.DPI300; // 匯出品質
                                            //options.Combine = true; // 合併檔案
                                            //options.HideCropBoundaries = false;                                            
                                            //ICollection<ElementId> views = new List<ElementId>() { viewInfo.view.Id }; // 準備要輸出的View
                                            //bool result = doc.Export(path, views.ToList(), options); // 匯出 PDF

                                            // Revit 2022以前使用的PDF列印設置
                                            ICollection<PrintSetting> printSettings = new FilteredElementCollector(doc).OfClass(typeof(PrintSetting)).Cast<PrintSetting>().ToList();
                                            ElementId chosePsid = (from x in printSettings where x.Name == optionCB.Text select x.Id).First<ElementId>();
                                            PrintSetting chosedPrintSetting = doc.GetElement(chosePsid) as PrintSetting;

                                            PrintManager printManager = doc.PrintManager;
                                            printManager.PrintRange = PrintRange.Current;
                                            //列印設定
                                            try
                                            {
                                                printManager.SelectNewPrintDriver("PDFCreator");
                                            }
                                            catch (Exception ex)
                                            {
                                                string error = ex.Message + "\n" + ex.ToString();
                                                printManager.SelectNewPrintDriver("Microsoft Print to PDF");
                                            }
                                            printManager.CombinedFile = true;
                                            printManager.PrintToFile = true;
                                            printManager.PrintSetup.CurrentPrintSetting = chosedPrintSetting;
                                            printManager.PrintToFileName = Path.Combine(path, viewInfo.picNumber + ".pdf"); //輸出位置
                                            printManager.Apply();
                                            printManager.SubmitPrint(viewInfo.view as View);
                                            GC.Collect();
                                            GC.WaitForPendingFinalizers();

                                            //ExportPDF exportPDF = new ExportPDF();
                                            //exportPDF.ExportToPDF(formUIApp);
                                        }
                                        catch (Exception ex)
                                        {
                                            string error = ex.Message + "\n" + ex.ToString();
                                            //TaskDialog.Show("ERROR", "Couldn't access PDF driver registry settings");
                                        }
                                    }

                                    trans.Commit();
                                }
                            }
                            catch (Exception ex)
                            {
                                TaskDialog.Show("Error", ex.Message + "\n" + ex.ToString());
                            }
                        }
                    }

                    // 計時結束 取得目前時間
                    DateTime timeEnd = DateTime.Now;
                    TimeSpan totalTime = timeEnd - timeStart;
                    TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 ");
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message + "\n" + ex.ToString());
            }

            Close(); // 關閉
        }
        // 選擇Excel檔
        private string ChooseExportPath(string path)
        {
            FolderBrowserDialog dilog = new FolderBrowserDialog();
            dilog.SelectedPath = path; // 預設路徑
            dilog.Description = "請選擇資料夾";
            if (dilog.ShowDialog() == DialogResult.OK)
            {
                trueOrFlase = true; // 有選擇路徑
                path = dilog.SelectedPath;
            }

            return path;
        }
        // 取消
        private void cancel_Click(object sender, EventArgs e)
        {
            Close(); // 關閉
        }
        // 更換格式
        private void formatCB_SelectedIndexChanged(object sender, EventArgs e)
        {
            List<string> options = (from x in formatOptionList
                                    where x.format.Equals(formatCB.Text)
                                    select x.options).FirstOrDefault();            
            optionCB.Items.Clear(); // 清除setupCB選項
            // 更新setupCB選項
            foreach(string option in options)
            {
                optionCB.Items.Add(option);
            }
            if (options.Count() > 0)
            {
                optionCB.Text = optionCB.Items[0].ToString();
            }
            else
            {
                optionCB.Text = "";
            }
        }
    }
}
