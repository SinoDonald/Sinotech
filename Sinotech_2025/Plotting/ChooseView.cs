using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using View = Autodesk.Revit.DB.View;
using ComboBox = System.Windows.Forms.ComboBox;
using TextBox = System.Windows.Forms.TextBox;

namespace Sinotech_2025.Plotting
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
        private readonly ComboBox[] nameParameters = new ComboBox[3];
        private readonly TextBox[] nameSeparators = new TextBox[2];
        private Label namePreview;
        private const string DefaultNameParameter = "圖框-電腦圖號";

        private void InitializeFileNaming()
        {
            SuspendLayout();
            ClientSize = new System.Drawing.Size(620, 650);
            MinimumSize = Size;
            label3.Text = "匯出檔名（最多三個圖紙參數）";
            label3.Location = new System.Drawing.Point(139, 13);

            var names = new SortedSet<string>(StringComparer.CurrentCulture);
            names.Add(DefaultNameParameter);
            foreach (var info in viewInfoList)
                foreach (Parameter parameter in info.view.Parameters)
                    if (parameter.StorageType != StorageType.None)
                        names.Add(parameter.Definition.Name);
            for (int i = 0; i < 3; i++)
            {
                var label = new Label { Text = "參數 " + (i + 1), AutoSize = true,
                    Location = new System.Drawing.Point(13 + i * 200, 70) };
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new System.Drawing.Point(13 + i * 200, 92), Width = 190 };
                if (i > 0) combo.Items.Add("（不使用）");
                combo.Items.AddRange(names.Cast<object>().ToArray());
                combo.SelectedItem = i == 0 ? DefaultNameParameter : "（不使用）";
                nameParameters[i] = combo;
                combo.SelectedIndexChanged += (sender, args) => UpdateNamePreview();
                Controls.Add(label);
                Controls.Add(combo);
                if (i < 2)
                {
                    var separatorLabel = new Label { Text = "連接 " + (i + 1), AutoSize = true,
                        Location = new System.Drawing.Point(13 + i * 200, 128) };
                    var separator = new TextBox { Text = "_", Width = 130, MaxLength = 30,
                        Location = new System.Drawing.Point(70 + i * 200, 124) };
                    nameSeparators[i] = separator;
                    separator.TextChanged += (sender, args) => UpdateNamePreview();
                    Controls.Add(separatorLabel);
                    Controls.Add(separator);
                }
            }
            namePreview = new Label { AutoEllipsis = true, Width = 590, Height = 45,
                Location = new System.Drawing.Point(13, 155) };
            Controls.Add(namePreview);
            treeView1.AfterSelect += (sender, args) => UpdateNamePreview();
            UpdateNamePreview();
            ResumeLayout(false);
            LayoutExportControls();
            ClientSizeChanged += (sender, args) => LayoutExportControls();
        }

        private void LayoutExportControls()
        {
            // 明確依 ClientSize 配置，避免 SuspendLayout 期間變更視窗尺寸
            // 造成設計工具的 Anchor 基準仍停留在原始窄版視窗。
            const int margin = 13;
            const int listTop = 205;
            int footerTop = ClientSize.Height - margin - 33;
            treeView1.SetBounds(margin, listTop, ClientSize.Width - margin * 2,
                Math.Max(100, footerTop - listTop - 12));
            treeView1.BorderStyle = BorderStyle.FixedSingle;
            treeView1.FullRowSelect = true;
            treeView1.HideSelection = false;
            optionCB.SetBounds(margin, footerTop + 4, 200, 25);
            cancel.SetBounds(ClientSize.Width - margin - 72, footerTop, 72, 33);
            sure.SetBounds(cancel.Left - 12 - 72, footerTop, 72, 33);
            if (namePreview != null) namePreview.Width = ClientSize.Width - margin * 2;
        }

        private string BuildFileName(ViewInfo info)
        {
            string result = "";
            for (int i = 0; i < nameParameters.Length; i++)
            {
                if (i > 0 && nameParameters[i].SelectedIndex == 0) continue;
                string parameterName = nameParameters[i].Text;
                Parameter parameter = info.view.LookupParameter(parameterName);
                string value = parameter == null ? null : parameter.StorageType == StorageType.String
                    ? parameter.AsString() : parameter.AsValueString();
                if (string.IsNullOrWhiteSpace(value))
                    value = (info.view as ViewSheet)?.SheetNumber ?? "Sheet_" + info.view.Id.Value;
                if (result.Length > 0) result += nameSeparators[i - 1].Text;
                result += value;
            }
            return SanitizeFileName(result, "Sheet_" + info.view.Id.Value);
        }

        private static string SanitizeFileName(string value, string fallback)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string name = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
            name = name.Trim().TrimEnd('.', ' ');
            if (name.Length == 0) name = fallback;
            string stem = name.Split('.')[0].TrimEnd(' ');
            string[] reserved = { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
                "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³" };
            if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
            return name;
        }

        private void UpdateNamePreview()
        {
            if (namePreview == null) return;
            var info = viewInfoList.FirstOrDefault(x => x.name == treeView1.SelectedNode?.Text)
                ?? viewInfoList.FirstOrDefault();
            namePreview.Text = info == null ? "沒有可匯出的圖紙" : "檔名預覽：" + BuildFileName(info)
                + "\n非法字元自動替換為 _；參數缺值使用圖紙號碼；重名自動加流水號。";
        }

        private static string ReserveFileName(string folder, string name, HashSet<string> usedNames)
        {
            // 保留副檔名、流水號及 Revit 附屬輸出檔名所需空間。
            int limit = Math.Min(160, 240 - folder.Length - 1 - 16);
            if (limit < 20) throw new IOException("匯出路徑太長，請選擇較短的資料夾路徑。");
            if (name.Length > limit) name = name.Substring(0, limit).TrimEnd('.', ' ');
            string candidate = name;
            int index = 2;
            while (usedNames.Contains(candidate) || Directory.EnumerateFileSystemEntries(folder)
                .Any(file => string.Equals(Path.GetFileNameWithoutExtension(file), candidate, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(file).StartsWith(candidate + "-", StringComparison.OrdinalIgnoreCase)))
                candidate = name + "_" + index++;
            usedNames.Add(candidate);
            return candidate;
        }

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

            InitializeFileNaming();
            CenterToScreen(); // 置中
        }
        // 查詢專案中DWG、DGN、PDF所擁有的選項
        private void FormatsOption(Document doc)
        {
            formatOptionList = new List<FormatOption>(); // 清空格式類型與選項
            string[] formats = new string[] { "DWG", "DGN", "PDF" };
            foreach (string format in formats)
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
                    foreach (PrintSetting printSetting in printSettings)
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
                        if (view is ViewSheet)
                        {
                            // 電腦圖號
                            try
                            {
                                string picNumber = string.Empty;
                                try { picNumber = view.LookupParameter("圖框-電腦圖號").AsString(); } catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

                                if (!String.IsNullOrEmpty(picNumber))
                                {
                                    // 如果名字有特殊字元時則替換為"_"
                                    foreach (char invalidChar in Path.GetInvalidFileNameChars())
                                    {
                                        picNumber = picNumber.Replace(invalidChar, '_');
                                    }
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
                                viewInfo.levelId = (int)view.GenLevel.Id.Value;
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
            chooseViewSheets = chooseViewSheets.Where(x => x != null).Distinct().ToList();
            if (chooseViewSheets.Count == 0)
            {
                TaskDialog.Show("匯出圖紙", "請至少勾選一張圖紙。");
                return;
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

                    var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // 將選擇要匯出的圖紙加入id中
                    foreach (ViewInfo viewInfo in chooseViewSheets)
                    {
                        // 先確認此視圖是否可Printed
                        if (viewInfo.view.CanBePrinted == true)
                        {
                            try
                            {
                                //// 開啟View
                                //formUIApp.ActiveUIDocument.ActiveView = viewInfo.view;
                                //// 關閉其他視圖
                                //View currView = formDoc.ActiveView;
                                //formUIApp.ActiveUIDocument.RequestViewChange(currView);
                                //IList<UIView> openViews = formUIApp.ActiveUIDocument.GetOpenUIViews();
                                //foreach (UIView openView in openViews)
                                //{
                                //    if (openView.ViewId != currView.Id)
                                //    {
                                //        openView.Close();
                                //    }
                                //}
                                // 執行交易：先寫入視圖/圖框的時間戳記
                                using (Transaction trans = new Transaction(doc, "更新時間戳記"))
                                {
                                    // 開始交易
                                    trans.Start();

                                    // 取得當前時間戳記 (yyyy/MM/dd HH:mm:ss)
                                    string timestamp = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                                    // 1. 寫入圖紙 (ViewSheet) 的「時間戳記」參數
                                    Parameter param = viewInfo.view.LookupParameter("時間戳記");
                                    if (param != null && !param.IsReadOnly)
                                    {
                                        param.Set(timestamp);
                                    }

                                    // 2. 寫入該圖紙上圖框 (TitleBlock) 的「時間戳記」參數 (若參數綁定於圖框品類)
                                    FilteredElementCollector titleBlocks = new FilteredElementCollector(doc, viewInfo.view.Id)
                                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                                        .WhereElementIsNotElementType();
                                    foreach (Element tb in titleBlocks)
                                    {
                                        Parameter tbParam = tb.LookupParameter("時間戳記");
                                        if (tbParam != null && !tbParam.IsReadOnly)
                                        {
                                            tbParam.Set(timestamp);
                                        }
                                    }

                                    doc.Regenerate();
                                    trans.Commit();
                                }

                                string exportName = ReserveFileName(path, BuildFileName(viewInfo), usedNames);
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
                                    doc.Export(path, exportName, viewSheetElementIds, dwgOptions);
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
                                    doc.Export(path, exportName, viewSheetElementIds, dgnOptions);
                                    GC.Collect();
                                    GC.WaitForPendingFinalizers();
                                }
                                else if (formatCB.Text.Equals("PDF"))
                                {
                                    try
                                    {
                                        // 建立PDF匯出選項
                                        PDFExportOptions options = new PDFExportOptions();
                                        string fileName = exportName; // 使用自訂參數組成的安全檔名
                                        options.FileName = fileName; // 直接指定檔名
                                        options.ColorDepth = ColorDepthType.BlackLine; // 色彩深度
                                        options.ExportQuality = PDFExportQualityType.DPI300; // 匯出品質
                                        options.Combine = true; // 合併檔案
                                        options.HideCropBoundaries = false;
                                        ICollection<ElementId> views = new List<ElementId>() { viewInfo.view.Id }; // 準備要輸出的View
                                        bool result = doc.Export(path, views.ToList(), options); // 匯出 PDF
                                    }
                                    catch (Exception ex)
                                    {
                                        TaskDialog.Show("PDF 匯出失敗", ex.Message);
                                    }
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
            foreach (string option in options)
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
