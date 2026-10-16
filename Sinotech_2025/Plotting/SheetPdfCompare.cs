using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Forms = System.Windows.Forms;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Sinotech_2025.Plotting
{
    /// <summary>
    /// 比對圖框實際顯示的完整圖名（含中英文、分頁）及完整圖號（含前綴）。
    /// Revit 2025 公開 API 無法可靠取得族群標籤的參數順序、前後綴及換行設定；
    /// 每種圖框只用一次沒有視圖的暫存圖紙校準參數，之後直接讀取 Sheet／圖框參數。
    /// 校準交易完全復原，不儲存模型。缺少文字層時使用局部 OCR 並要求人工覆核。
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SheetPdfCompare : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                TaskDialog.Show("圖紙比對", "請先開啟 Revit 專案。");
                return Result.Cancelled;
            }
            using var picker = new Forms.OpenFileDialog { Filter = "PDF 圖說 (*.pdf)|*.pdf", Title = "選擇要比對的 PDF 圖說" };
            var owner = new WindowOwner(commandData.Application.MainWindowHandle);
            if (picker.ShowDialog(owner) != Forms.DialogResult.OK) return Result.Cancelled;

            string temp = Path.Combine(Path.GetTempPath(), "SinotechSheetCompare", Guid.NewGuid().ToString("N"));
            var rows = new DataTable();
            foreach (string column in new[] { "專案名稱", "Revit圖紙ID", "Revit圖紙圖號", "Revit圖紙名稱", "圖框族群／類型", "圖框完整圖號", "圖框完整圖名", "結果", "PDF頁碼", "說明" })
                rows.Columns.Add(column);
            rows.Columns.Add("參數組合");
            var pdfRows = new DataTable();
            foreach (string column in new[] { "PDF頁碼", "圖號", "圖名", "辨識結果" }) pdfRows.Columns.Add(column);
            pdfRows.Columns.Add("人工確認", typeof(bool)).DefaultValue = false;
            var indexRows = SheetDrawingIndex.CreateTable();
            var indexRaw = new StringBuilder();
            var notices = new List<string>();
            try
            {
                Directory.CreateDirectory(temp);
                string project = doc.ProjectInformation.get_Parameter(BuiltInParameter.PROJECT_NAME)?.AsString();
                if (string.IsNullOrWhiteSpace(project)) project = doc.Title;
                List<ViewSheet> sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>().OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase).ToList();
                if (sheets.Count == 0) { TaskDialog.Show("圖紙比對", "專案中沒有圖紙。"); return Result.Cancelled; }
                var model = new List<(DataRow Row, FrameText Frame)>();
                using var progress = new Forms.Form { Text = "圖紙比對中", Width = 640, Height = 130,
                    StartPosition = Forms.FormStartPosition.CenterScreen, ControlBox = false };
                var label = new Forms.Label { Dock = Forms.DockStyle.Fill, Padding = new Forms.Padding(15) };
                progress.Controls.Add(label);
                progress.Show(owner);
                void Update(string text) { label.Text = text; label.Refresh(); progress.Refresh(); }

                var titleblocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .WhereElementIsNotElementType().OfType<FamilyInstance>().ToLookup(x => x.OwnerViewId);
                var recipes = new Dictionary<string, TitleBlockRecipe>();
                var recipeErrors = new Dictionary<string, string>();
                for (int i = 0; i < sheets.Count; i++)
                {
                    ViewSheet sheet = sheets[i];
                    Update($"讀取 Revit 圖框 {i + 1}/{sheets.Count}：{sheet.SheetNumber}\n依圖框參數組合直接讀取圖紙資料。");
                    var blocks = titleblocks[sheet.Id].ToList();
                    DataRow row = rows.Rows.Add(project, sheet.Id.Value.ToString(), sheet.SheetNumber, sheet.Name,
                        string.Join("; ", blocks.Select(b => b.Symbol.FamilyName + " / " + b.Symbol.Name)), "", "", "待確認", "", "");
                    if (sheet.IsPlaceholder || !sheet.CanBePrinted || blocks.Count != 1)
                    {
                        row["說明"] = sheet.IsPlaceholder ? "預留圖紙，無法取得圖框顯示內容。" :
                            blocks.Count == 0 ? "沒有圖框。" : blocks.Count > 1 ? "有多個圖框，需確認要比對哪一個。" : "圖紙無法列印。";
                        continue;
                    }
                    try
                    {
                        string key = SheetTitleBlockParameters.CacheKey(blocks[0]);
                        if (recipeErrors.TryGetValue(key, out string failure)) throw new InvalidOperationException(failure);
                        if (!recipes.TryGetValue(key, out TitleBlockRecipe recipe))
                        {
                            Update($"首次校準圖框參數：{blocks[0].Symbol.FamilyName} / {blocks[0].Symbol.Name}\n只輸出暫存圖框，不輸出圖紙上的視圖；同類型之後直接讀參數。");
                            try { recipe = SheetTitleBlockParameters.Discover(doc, sheet, blocks[0], temp); recipes.Add(key, recipe); }
                            catch (Exception ex) { recipeErrors.Add(key, ex.Message); throw; }
                        }
                        FrameText frame = recipe.Read(doc, sheet, blocks[0]);
                        if (string.IsNullOrWhiteSpace(frame.Number) || string.IsNullOrWhiteSpace(frame.Title))
                            throw new InvalidOperationException("圖框的圖名或圖號參數內容為空白。");
                        row["圖框完整圖號"] = frame.Number;
                        row["圖框完整圖名"] = frame.Title;
                        row["參數組合"] = recipe.Description;
                        model.Add((row, frame));
                    }
                    catch (Exception ex) { row["說明"] = ex.Message; }
                }

                var target = new List<(int Page, FrameText Frame)>();
                var allPdfFrames = new List<(int Page, FrameText Frame)>();
                var indexPages = new List<(int Page, FrameText Frame)>();
                var ocrHints = model.Select(m => m.Frame).ToList();
                using var ocr = new SheetPdfOcr(temp);
                using (var pdf = PdfDocument.Open(picker.FileName))
                {
                    for (int i = 1; i <= pdf.NumberOfPages; i++)
                    {
                        Update($"讀取附件 PDF {i}/{pdf.NumberOfPages}\n僅比對與模型圖框欄格樣式相符的圖紙。");
                        try
                        {
                            var page = pdf.GetPage(i);
                            var frames = SheetFrameReader.Read(page, out string reason);
                            bool noFrame = frames.Count == 0 && SheetFrameReader.IsDefinitelyUnframed(page, ocrHints, reason);
                            if (noFrame) continue;
                            if (frames.Count == 0 && ocrHints.Count > 0)
                            {
                                Update($"加強辨識 PDF 第 {i}/{pdf.NumberOfPages} 頁\n圖名／圖號欄格局部 OCR，擷取結果仍需人工確認。");
                                frames = ocr.Read(picker.FileName, page, ocrHints, out string ocrReason);
                                if (frames.Count == 0) reason += " " + ocrReason;
                                else notices.Add($"PDF 第 {i} 頁使用局部 OCR：{reason} 請覆核擷取內容。");
                            }
                            bool isIndex = SheetDrawingIndex.IsIndex(page, frames);
                            if (!isIndex && frames.Count == 0 && !string.IsNullOrEmpty(reason) && page.Paths.Count > 1000)
                            {
                                try { isIndex = SheetDrawingIndex.HasOcrHeading(picker.FileName, page, ocr); }
                                catch (Exception ex) { notices.Add($"PDF 第 {i} 頁目錄標題辨識失敗：{ex.Message}"); }
                            }
                            if (isIndex) indexPages.Add((i, frames.FirstOrDefault()));
                            if (frames.Count == 0)
                            {
                                if (!string.IsNullOrEmpty(reason)) notices.Add($"PDF 第 {i} 頁：{reason}");
                                pdfRows.Rows.Add(i, "", "", "待確認：" + (string.IsNullOrEmpty(reason) ? "無法確認圖框內容。" : reason));
                                continue;
                            }
                            foreach (FrameText frame in frames)
                            {
                                allPdfFrames.Add((i, frame));
                                bool same = model.Any(m => m.Frame.SameLayout(frame));
                                pdfRows.Rows.Add(i, frame.Number, frame.Title, frame.OcrCandidate ? $"待確認：局部 OCR（{frame.OcrScale:0}倍）已擷取，請覆核後勾選人工確認" :
                                    same ? "納入比對" : "略過：與模型圖框欄格樣式不同");
                                if (!frame.OcrCandidate)
                                {
                                    ocrHints.Add(frame);
                                    if (same) target.Add((i, frame));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            notices.Add($"PDF 第 {i} 頁讀取失敗：{ex.Message}");
                            pdfRows.Rows.Add(i, "", "", "待確認：" + ex.Message);
                        }
                    }
                    foreach (var entry in indexPages)
                    {
                        try
                        {
                            var entries = SheetDrawingIndex.Read(picker.FileName, pdf.GetPage(entry.Page), entry.Frame, ocr,
                                allPdfFrames.Select(f => f.Frame).Concat(ocrHints), Update, out string raw);
                            SheetDrawingIndex.Add(indexRows, entry.Page, entries);
                            indexRaw.AppendLine(raw).AppendLine();
                            if (entries.Count == 0) indexRows.Rows.Add(entry.Page, "", "", "", "", "待確認", "目錄未能擷取；可人工新增項目", false, true);
                            notices.Add($"PDF 第 {entry.Page} 頁圖目錄：擷取 {entries.Count} 筆；OCR 項目需核對是否有漏列。");
                        }
                        catch (Exception ex)
                        {
                            indexRows.Rows.Add(entry.Page, "", "", "", "", "待確認", ex.Message, false, true);
                            notices.Add($"PDF 第 {entry.Page} 頁圖目錄擷取失敗：{ex.Message}");
                        }
                    }
                }
                void Compare()
                {
                    SheetPdfComparison.Apply(model, target, pdfRows);
                    SheetDrawingIndex.Compare(indexRows, allPdfFrames, pdfRows);
                }
                Compare();
                progress.Close();
                ShowResults(owner, rows, pdfRows, indexRows, indexRaw.ToString(), picker.FileName, notices, Compare);
                return Result.Succeeded;
            }
            catch (Exception ex) { message = ex.Message; TaskDialog.Show("圖紙比對失敗", ex.Message); return Result.Failed; }
            finally
            {
                // 只清除此命令建立的 GUID 目錄，不刪除既有使用者檔案。
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void ShowResults(Forms.IWin32Window owner, DataTable rows, DataTable pdfRows, DataTable indexRows, string indexRaw,
            string path, List<string> notices, Action compare)
        {
            using var form = new Forms.Form { Text = "圖紙比對結果", Width = 1250, Height = 760,
                StartPosition = Forms.FormStartPosition.CenterScreen };
            var tabs = new Forms.TabControl { Dock = Forms.DockStyle.Fill };
            Forms.DataGridView Grid(DataTable table) => new Forms.DataGridView { Dock = Forms.DockStyle.Fill,
                DataSource = table, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = Forms.DataGridViewAutoSizeColumnsMode.DisplayedCells,
                SelectionMode = Forms.DataGridViewSelectionMode.FullRowSelect, RowHeadersVisible = false };
            var modelTab = new Forms.TabPage("Revit 全部圖紙"); modelTab.Controls.Add(Grid(rows)); tabs.TabPages.Add(modelTab);
            var pdfTab = new Forms.TabPage("PDF 頁面明細／人工確認");
            var pdfGrid = Grid(pdfRows);
            pdfGrid.ReadOnly = false;
            pdfTab.Controls.Add(pdfGrid); tabs.TabPages.Add(pdfTab);
            pdfGrid.DataBindingComplete += (_, __) =>
            {
                foreach (Forms.DataGridViewColumn column in pdfGrid.Columns)
                {
                    column.ReadOnly = column.Name != "圖號" && column.Name != "圖名" && column.Name != "人工確認";
                    column.MinimumWidth = column.Name == "圖名" ? 300 : column.Name == "圖號" ? 160 : column.Name == "辨識結果" ? 400 : 70;
                }
            };
            var indexTab = new Forms.TabPage("圖目錄／PDF缺頁檢查");
            var indexGrid = Grid(indexRows);
            indexGrid.ReadOnly = false; indexGrid.AllowUserToAddRows = true; indexTab.Controls.Add(indexGrid); tabs.TabPages.Add(indexTab);
            indexGrid.DataBindingComplete += (_, __) =>
            {
                foreach (Forms.DataGridViewColumn column in indexGrid.Columns)
                {
                    column.ReadOnly = !new[] { "目錄頁碼", "目錄圖號", "目錄圖名", "人工確認" }.Contains(column.Name);
                    column.MinimumWidth = column.Name == "目錄圖名" ? 300 : column.Name == "目錄圖號" ? 160 : 80;
                    if (column.Name == "OCR擷取") column.Visible = false;
                }
            };
            var rawTab = new Forms.TabPage("目錄擷取原文");
            rawTab.Controls.Add(new Forms.TextBox { Dock = Forms.DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = Forms.ScrollBars.Both, WordWrap = false, Text = indexRaw.Length > 0 ? indexRaw : "未辨識到圖目錄頁面。" });
            tabs.TabPages.Add(rawTab);
            var detailTab = new Forms.TabPage("辨識說明");
            detailTab.Controls.Add(new Forms.TextBox { Dock = Forms.DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = Forms.ScrollBars.Both, Text = "PDF：" + path + Environment.NewLine +
                "比較圖框實際顯示的中英文完整內容；忽略大小寫、空白、換行及全半形差異，保留內容和標點差異。" + Environment.NewLine +
                "每種圖框只校準一次參數組合，以沒有模型視圖的暫存圖框探測，並完全復原交易；之後直接讀取各張圖紙參數。" + Environment.NewLine +
                "同樣式以圖名／圖號欄格的相對大小與位置判斷。文字转線條、缺字頁使用 Windows 繁體中文局部 OCR；OCR 結果仍需人工確認。" + Environment.NewLine +
                "人工覆核：於 PDF 頁面明細填入圖框完整圖名／圖號並勾選人工確認，再按重新比對。" + Environment.NewLine +
                "若為非相關頁面，清空圖名及圖號並勾選人工確認。每次開啟命令需重新覆核；CSV 保留覆核欄位。" + Environment.NewLine +
                "圖目錄按圖號分類列出完整圖名；內容分類依圖名字詞推定。目錄中的圖號不當作實際圖紙存在的證據；OCR 目錄需覆核，PDF 有未確認頁面時不直接判定缺頁。" + Environment.NewLine +
                "可在目錄分頁補入漏辨識的項目，再套用人工確認。OCR 比較 4／6／10 倍原圖及增強圖，逐行選取讀值，仍需人工覆核。" + Environment.NewLine +
                "確認沒有圖框的頁面直接略過，不列於頁面明細；無法排除圖框的掃描頁或辨識失敗頁仍保留待確認。" + Environment.NewLine +
                string.Join(Environment.NewLine, notices) });
            tabs.TabPages.Add(detailTab);
            var top = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Top, Height = 70, Padding = new Forms.Padding(8) };
            var summary = new Forms.Label { AutoSize = true };
            void Summary() => summary.Text = string.Join("　", rows.AsEnumerable().GroupBy(r => r.Field<string>("結果"))
                .Select(g => g.Key + "：" + g.Count())) + $"　目錄項目：{indexRows.Rows.Count}";
            Summary(); top.Controls.Add(summary);
            var filter = new Forms.CheckBox { Text = "只看未相符／待確認", AutoSize = true };
            filter.CheckedChanged += (_, __) =>
            {
                rows.DefaultView.RowFilter = filter.Checked ? "結果 <> '已匯出'" : "";
                indexRows.DefaultView.RowFilter = filter.Checked ? "比對結果 <> '已收錄' AND 比對結果 <> '已排除'" : "";
            };
            top.Controls.Add(filter);
            var recalculate = new Forms.Button { Text = "套用人工確認並重新比對", AutoSize = true };
            recalculate.Click += (_, __) =>
            {
                pdfGrid.EndEdit(); form.BindingContext[pdfRows].EndCurrentEdit();
                indexGrid.EndEdit(); form.BindingContext[indexRows].EndCurrentEdit();
                foreach (DataRow row in pdfRows.Rows)
                    if (row.Field<bool>("人工確認") && string.IsNullOrWhiteSpace(row.Field<string>("圖號")) != string.IsNullOrWhiteSpace(row.Field<string>("圖名")))
                    {
                        Forms.MessageBox.Show(form, "第 " + row["PDF頁碼"] + " 頁：相關圖紙需填妥圖名及圖號；非相關頁面請將兩欄都清空。", "人工確認");
                        return;
                    }
                foreach (DataRow row in indexRows.Rows)
                    if (row.Field<bool>("人工確認") && string.IsNullOrWhiteSpace(row.Field<string>("目錄圖號")) != string.IsNullOrWhiteSpace(row.Field<string>("目錄圖名")))
                    {
                        Forms.MessageBox.Show(form, "目錄項目需填妥圖名及圖號；要排除的項目請將兩欄都清空。", "人工確認"); return;
                    }
                compare(); Summary(); tabs.SelectedTab = modelTab;
            };
            top.Controls.Add(recalculate);
            var save = new Forms.Button { Text = "匯出 CSV", AutoSize = true };
            save.Click += (_, __) =>
            {
                using var dialog = new Forms.SaveFileDialog { Filter = "CSV (*.csv)|*.csv",
                    FileName = tabs.SelectedTab == indexTab || tabs.SelectedTab == rawTab ? "圖目錄缺頁檢查.csv" : "圖紙比對結果.csv" };
                if (dialog.ShowDialog(form) != Forms.DialogResult.OK) return;
                try
                {
                    pdfGrid.EndEdit(); form.BindingContext[pdfRows].EndCurrentEdit();
                    indexGrid.EndEdit(); form.BindingContext[indexRows].EndCurrentEdit();
                    DataTable table = tabs.SelectedTab == indexTab || tabs.SelectedTab == rawTab ? indexRows : tabs.SelectedTab == pdfTab ? pdfRows : rows;
                    string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
                    using var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true));
                    writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => Quote(c.ColumnName))));
                    foreach (DataRow row in table.Rows)
                        writer.WriteLine(string.Join(",", row.ItemArray.Select(v => Quote(Convert.ToString(v)))));
                }
                catch (Exception ex) { Forms.MessageBox.Show(form, ex.Message, "CSV 匯出失敗"); }
            };
            top.Controls.Add(save);
            form.Controls.Add(tabs); form.Controls.Add(top); form.ShowDialog(owner);
        }

        private sealed class WindowOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; }
            public WindowOwner(IntPtr handle) { Handle = handle; }
        }
    }

    internal static class SheetPdfComparison
    {
        internal static void Apply(List<(DataRow Row, FrameText Frame)> model, List<(int Page, FrameText Frame)> target, DataTable pdfRows)
        {
            var reviewed = pdfRows.AsEnumerable().Where(r => r.Field<bool>("人工確認")).ToList();
            bool incomplete = pdfRows.AsEnumerable().Any(r => !r.Field<bool>("人工確認") && r.Field<string>("辨識結果").StartsWith("待確認"));
            var reviewedPages = reviewed.Select(r => int.Parse(r.Field<string>("PDF頁碼"))).ToHashSet();
            var active = target.Where(t => !reviewedPages.Contains(t.Page)).ToList();
            foreach (var row in reviewed.Where(r => !string.IsNullOrWhiteSpace(r.Field<string>("圖號"))))
                // 人工確認表示已確認為相關圖框；不再以自動幾何判斷阻擋此頁。
                active.Add((int.Parse(row.Field<string>("PDF頁碼")), new FrameText {
                    Number = row.Field<string>("圖號"), Title = row.Field<string>("圖名"), ManuallyReviewed = true }));
            foreach (var entry in model)
            {
                var sameNumber = active.Where(t => (t.Frame.ManuallyReviewed || entry.Frame.SameLayout(t.Frame)) &&
                    SheetFrameReader.Normalize(t.Frame.Number) == SheetFrameReader.Normalize(entry.Frame.Number)).ToList();
                var matches = sameNumber.Where(t => SheetFrameReader.Normalize(t.Frame.Title) == SheetFrameReader.Normalize(entry.Frame.Title)).ToList();
                entry.Row["PDF頁碼"] = "";
                if (matches.Count > 0)
                {
                    entry.Row["結果"] = "已匯出";
                    entry.Row["PDF頁碼"] = string.Join(", ", matches.Select(t => t.Page).Distinct());
                    entry.Row["說明"] = matches.Count > 1 ? "PDF 中有重複的圖名＋圖號。" : "圖框完整圖名＋圖號相符。";
                }
                else if (sameNumber.Count > 0)
                {
                    entry.Row["結果"] = "圖名不一致";
                    entry.Row["PDF頁碼"] = string.Join(", ", sameNumber.Select(t => t.Page).Distinct());
                    entry.Row["說明"] = "相同圖號的 PDF 圖名：" + string.Join(" | ", sameNumber.Select(t => t.Frame.Title).Distinct());
                }
                else
                {
                    bool noScope = active.Count == 0 && reviewed.Count == 0;
                    entry.Row["結果"] = incomplete || noScope ? "待確認" : "未匯出";
                    entry.Row["說明"] = incomplete ? "PDF 有未人工確認的待確認頁面；未找到相符組合，暫不判定未匯出。" :
                        noScope ? "PDF 沒有與模型相同欄格樣式的圖框；請確認頁面範圍。" : "相同圖框樣式的 PDF 圖紙中，沒有此圖名＋圖號。";
                }
            }
        }
    }

    internal sealed class FrameText
    {
        public string Title, Number;
        public bool ManuallyReviewed;
        public bool OcrCandidate;
        public double PageWidth, PageHeight;
        public double OcrScale;
        public PdfRectangle TitleCell, NumberCell;
        // 使用相對欄格幾何，容許 PDF 出圖縮放與不同紙張尺寸；不固定右下角座標。
        public bool SameLayout(FrameText other)
        {
            double[] Shape(FrameText f) => new[] { f.TitleCell.Width / f.NumberCell.Width,
                f.TitleCell.Height / f.NumberCell.Height,
                (f.TitleCell.Left - f.NumberCell.Left) / f.TitleCell.Width,
                (f.TitleCell.Bottom - f.NumberCell.Top) / f.TitleCell.Width };
            return Shape(this).Zip(Shape(other), (a, b) => Math.Abs(a - b) <= 0.08 * Math.Max(1, Math.Abs(a))).All(x => x);
        }
    }

    /// <summary>文字與封閉欄格一起辨識，避免把目錄、回覆表、剖面索引中的圖號當作出圖。</summary>
    internal static class SheetFrameReader
    {
        internal static string Normalize(string value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC)
            .Replace('–', '-').Replace('—', '-').Replace('−', '-'), @"\s+", "").ToUpperInvariant();
        private static string LabelKey(string value) => Regex.Replace(Normalize(value), @"[\p{P}\s]", "");
        private static bool IsTitle(string text) => new[] { "TITLE", "DRAWINGTITLE", "圖名", "圖名稱", "图名" }.Contains(LabelKey(text));
        private static bool IsNumber(string text) => new[] { "DWGNO", "DRAWINGNO", "DRAWINGNUMBER", "圖號", "图号" }.Contains(LabelKey(text));

        internal static List<FrameText> Read(Page page, out string reason)
        {
            reason = "";
            var result = new List<FrameText>();
            if (page.Letters.Count == 0)
            {
                reason = page.Paths.Count > 1000 ? "沒有文字層，文字可能已轉成線條外框，需局部 OCR／人工確認。" :
                    "沒有文字層，可能為影像頁或空白頁，需局部 OCR／人工確認。";
                return result;
            }
            var pieces = Pieces(page.Letters);
            var titleLabels = pieces.Where(p => IsTitle(p.Text)).ToList();
            var numberLabels = pieces.Where(p => IsNumber(p.Text)).ToList();
            if (titleLabels.Count == 0 || numberLabels.Count == 0)
            {
                if (titleLabels.Count > 0 || numberLabels.Count > 0) reason = "只找到部分圖框標題，無法完整辨識圖名與圖號。";
                return result;
            }
            var (horizontal, vertical) = Edges(page);
            var titles = titleLabels.Select(p => Cell(p.Box, horizontal, vertical)).Where(c => c.HasValue).Select(c => c.Value).ToList();
            var numbers = numberLabels.Select(p => Cell(p.Box, horizontal, vertical)).Where(c => c.HasValue).Select(c => c.Value).ToList();
            foreach (var title in titles)
            foreach (var number in numbers)
            {
                // 圖號必須在圖名下方且同一圖框；不能跨表格配對。
                if (number.Top > title.Bottom + 2 || title.Bottom - number.Top > title.Width * 0.35 ||
                    Math.Abs(title.Left - number.Left) > title.Width * 0.12 ||
                    title.Width / number.Width < 0.65 || title.Width / number.Width > 3) continue;
                string titleText = Content(page.Letters, title);
                string numberText = Content(page.Letters, number);
                if (string.IsNullOrWhiteSpace(titleText) || string.IsNullOrWhiteSpace(numberText) ||
                    !Regex.IsMatch(numberText, @"\d") || BadText(titleText + numberText)) continue;
                if (!result.Any(f => Math.Abs(f.TitleCell.Left - title.Left) < 1 && Math.Abs(f.TitleCell.Bottom - title.Bottom) < 1))
                    result.Add(new FrameText { Title = titleText, Number = numberText, TitleCell = title, NumberCell = number,
                        PageWidth = page.Width, PageHeight = page.Height });
            }
            if (result.Count == 0) reason = "有圖框標題，但欄格線、字元對照或圖名／圖號位置無法可靠辨識。";
            return result;
        }

        internal static (PdfRectangle Title, PdfRectangle Number)? ProjectCells(Page page, FrameText hint, out string diagnostic)
        {
            var (horizontal, vertical) = Edges(page);
            // 無文字層時，字形外框也是短直線；需先排除它們，才能找到真正的欄格邊界。
            double titleWidth = hint.TitleCell.Width * page.Width / hint.PageWidth;
            horizontal = horizontal.Where(e => e.To - e.From >= titleWidth * 0.4).ToList();
            vertical = vertical.Where(e => e.To - e.From >= Math.Min(8, hint.NumberCell.Height * page.Height / hint.PageHeight * 0.65)).ToList();
            diagnostic = "";
            PdfRectangle Project(PdfRectangle box) => new PdfRectangle(box.Left * page.Width / hint.PageWidth,
                box.Bottom * page.Height / hint.PageHeight, box.Right * page.Width / hint.PageWidth, box.Top * page.Height / hint.PageHeight);
            PdfRectangle? Find(PdfRectangle projected)
            {
                double x = (projected.Left + projected.Right) / 2, y = (projected.Bottom + projected.Top) / 2;
                return Cell(new PdfRectangle(x - 0.1, y - 0.1, x + 0.1, y + 0.1), horizontal, vertical);
            }
            var title = Find(Project(hint.TitleCell));
            if (!title.HasValue) { diagnostic = "找不到圖名封閉欄格。"; return null; }
            // 不同圖框的圖名格高度可能不同。圖號改從圖名格下方逐格找，避免落到 Tender No.。
            double expectedWidth = title.Value.Width * hint.NumberCell.Width / hint.TitleCell.Width;
            double xNumber = title.Value.Left + expectedWidth / 2;
            var boundaries = horizontal.Where(e => e.From <= xNumber && e.To >= xNumber &&
                e.Position <= title.Value.Bottom + 1 && e.Position >= title.Value.Bottom - title.Value.Width * 0.5)
                .Select(e => e.Position).Distinct().OrderByDescending(y => y).ToList();
            var candidates = new List<PdfRectangle>();
            for (int i = 0; i + 1 < boundaries.Count; i++)
            {
                double y = (boundaries[i] + boundaries[i + 1]) / 2;
                var number = Cell(new PdfRectangle(xNumber - 0.1, y - 0.1, xNumber + 0.1, y + 0.1), horizontal, vertical);
                if (!number.HasValue || number.Value.Top > title.Value.Bottom + 1 ||
                    Math.Abs(title.Value.Left - number.Value.Left) > title.Value.Width * 0.12 ||
                    Math.Abs(number.Value.Width - expectedWidth) > expectedWidth * 0.25) continue;
                candidates.Add(number.Value);
            }
            if (candidates.Count == 0) { diagnostic = "找不到圖號封閉欄格。"; return null; }
            return (title.Value, candidates.OrderBy(c => Math.Abs(c.Width - expectedWidth)).ThenBy(c => c.Bottom).First());
        }

        // 沒有文字不代表沒有圖框。影像頁與未知欄格仍保留待確認。
        internal static bool IsDefinitelyUnframed(Page page, IEnumerable<FrameText> hints, string reason)
        {
            if (page.GetImages().Any()) return false;
            if (!string.IsNullOrEmpty(reason) && page.Letters.Count > 0) return false;
            var layouts = new List<FrameText>();
            foreach (var hint in hints.Where(h => h.PageWidth > 0 && h.PageHeight > 0))
            {
                if (layouts.Any(h => h.SameLayout(hint))) continue;
                layouts.Add(hint);
                if (ProjectCells(page, hint, out _).HasValue) return false;
            }
            // 可讀文字卻沒有任何圖框標題，或確實空白的頁面，直接略過。
            return page.Letters.Count > 0 || page.Paths.Count == 0;
        }

        internal static string ReliableCellContent(Page page, PdfRectangle cell)
        {
            string text = Content(page.Letters, cell);
            return string.IsNullOrWhiteSpace(text) || BadText(text) ? null : text;
        }

        private static (List<Edge> Horizontal, List<Edge> Vertical) Edges(Page page)
        {
            var horizontal = new List<Edge>(); var vertical = new List<Edge>();
            // 曲線、文字外框不當作欄格線；只取實際描邊的直線與矩形封閉線。
            foreach (var path in page.Paths.Where(p => p.IsStroked || p.IsFilled))
            foreach (var sub in path)
            {
                PdfPoint? start = null, last = null;
                foreach (var command in sub.Commands)
                {
                    if (command is PdfSubpath.Move move) { start = move.Location; last = start; }
                    else if (command is PdfSubpath.Line line) { Add(line.From, line.To); last = line.To; }
                    else if (command is PdfSubpath.Close && start.HasValue && last.HasValue) Add(last.Value, start.Value);
                    else last = null;
                }
                // PDF 填色子路徑即使沒有 close 指令也隱含封閉。CAD 常以兩個三角形畫粗框線。
                if (path.IsFilled && start.HasValue && last.HasValue) Add(last.Value, start.Value);
            }
            void Add(PdfPoint a, PdfPoint b)
            {
                if (Math.Abs(a.Y - b.Y) < 0.2 && Math.Abs(a.X - b.X) > 2)
                    horizontal.Add(new Edge((a.Y + b.Y) / 2, Math.Min(a.X, b.X), Math.Max(a.X, b.X)));
                if (Math.Abs(a.X - b.X) < 0.2 && Math.Abs(a.Y - b.Y) > 2)
                    vertical.Add(new Edge((a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y)));
            }
            return (horizontal, vertical);
        }

        private static bool BadText(string text) => text.Any(c => char.IsControl(c) && !char.IsWhiteSpace(c)) || text.Contains('\uFFFD') || text.Contains('?') ||
            text.Contains("(cid:") || text.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.PrivateUse);

        internal sealed class Piece
        {
            public string Text;
            public PdfRectangle Box;
        }
        private sealed class Edge
        {
            public double Position, From, To;
            public Edge(double position, double from, double to) { Position = position; From = from; To = to; }
        }

        private static PdfRectangle? Cell(PdfRectangle label, List<Edge> horizontal, List<Edge> vertical)
        {
            double x = (label.Left + label.Right) / 2, y = (label.Bottom + label.Top) / 2;
            var hs = horizontal.Where(e => e.From < x && e.To > x).ToList();
            var vs = vertical.Where(e => e.From < y && e.To > y).ToList();
            double left = vs.Where(e => e.Position < label.Left).Select(e => e.Position).DefaultIfEmpty(double.NaN).Max();
            double right = vs.Where(e => e.Position > label.Right).Select(e => e.Position).DefaultIfEmpty(double.NaN).Min();
            double bottom = hs.Where(e => e.Position < label.Bottom).Select(e => e.Position).DefaultIfEmpty(double.NaN).Max();
            double top = hs.Where(e => e.Position > label.Top).Select(e => e.Position).DefaultIfEmpty(double.NaN).Min();
            if (double.IsNaN(left + right + bottom + top) || right <= left || top <= bottom) return null;
            const double tol = 1.5;
            // 各邊可以由數段直線拼接，但必須覆蓋整個欄格，避免把表格其他格的線誤接。
            bool Covers(List<Edge> edges, double at, double from, double to)
            {
                double cursor = from;
                foreach (var e in edges.Where(e => Math.Abs(e.Position - at) <= tol).OrderBy(e => e.From))
                {
                    if (e.To < cursor - tol) continue;
                    if (e.From > cursor + tol) return false;
                    cursor = Math.Max(cursor, e.To);
                    if (cursor >= to - tol) return true;
                }
                return false;
            }
            if (!Covers(horizontal, bottom, left, right) || !Covers(horizontal, top, left, right) ||
                !Covers(vertical, left, bottom, top) || !Covers(vertical, right, bottom, top)) return null;
            return new PdfRectangle(left, bottom, right, top);
        }

        private static string Content(IReadOnlyList<Letter> letters, PdfRectangle cell)
        {
            var inside = letters.Where(l =>
            {
                double x = (l.BoundingBox.Left + l.BoundingBox.Right) / 2;
                double y = (l.BoundingBox.Bottom + l.BoundingBox.Top) / 2;
                return x > cell.Left && x < cell.Right && y > cell.Bottom && y < cell.Top;
            });
            return string.Join(Environment.NewLine, Pieces(inside).Where(p => !IsTitle(p.Text) && !IsNumber(p.Text)).Select(p => p.Text));
        }

        internal static List<Piece> Pieces(IEnumerable<Letter> source)
        {
            // 依基線分行；每行再以大間距分出左側標題與右側內容，保留中文及英文內部空白。
            var lines = new List<List<Letter>>();
            foreach (var letter in source.Where(l => !string.IsNullOrWhiteSpace(l.Value))
                .OrderByDescending(l => l.StartBaseLine.Y).ThenBy(l => l.BoundingBox.Left))
            {
                double tolerance = Math.Max(0.6, letter.BoundingBox.Height * 0.25);
                var line = lines.LastOrDefault();
                if (line == null || Math.Abs(line[0].StartBaseLine.Y - letter.StartBaseLine.Y) > tolerance)
                    lines.Add(new List<Letter> { letter });
                else line.Add(letter);
            }
            var result = new List<Piece>();
            foreach (var line in lines)
            {
                var run = new List<Letter>();
                foreach (var letter in line.OrderBy(l => l.BoundingBox.Left))
                {
                    if (run.Count > 0 && letter.BoundingBox.Left - run.Last().BoundingBox.Right >
                        Math.Max(letter.BoundingBox.Height, run.Last().BoundingBox.Height) * 1.8)
                    { Emit(run); run.Clear(); }
                    // 相同位置重複文字只留一次（部分 CAD PDF 有重疊文字）。
                    if (!run.Any(l => l.Value == letter.Value && Math.Abs(l.BoundingBox.Left - letter.BoundingBox.Left) < 0.1 &&
                        Math.Abs(l.StartBaseLine.Y - letter.StartBaseLine.Y) < 0.1)) run.Add(letter);
                }
                Emit(run);
            }
            void Emit(List<Letter> run)
            {
                if (run.Count == 0) return;
                var text = new StringBuilder(); Letter last = null;
                foreach (var l in run)
                {
                    if (last != null && l.StartBaseLine.X - last.EndBaseLine.X >
                        Math.Min(l.BoundingBox.Height, last.BoundingBox.Height) * 0.15) text.Append(' ');
                    text.Append(l.Value); last = l;
                }
                result.Add(new Piece { Text = text.ToString().Trim(), Box = new PdfRectangle(run.Min(l => l.BoundingBox.Left),
                    run.Min(l => l.BoundingBox.Bottom), run.Max(l => l.BoundingBox.Right), run.Max(l => l.BoundingBox.Top)) });
            }
            return result;
        }
    }
}
