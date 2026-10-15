using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;

namespace Sinotech.Plotting
{
    internal sealed class IndexText
    {
        internal string Text;
        internal double X, Y, Width, Height;
        internal int Variant;
    }
    internal sealed class DrawingIndexEntry
    {
        internal string Number, Title, Note;
        internal bool Ocr;
    }
    internal static class SheetDrawingIndex
    {
        internal static DataTable CreateTable()
        {
            var table = new DataTable();
            foreach (string c in new[] { "目錄頁碼", "圖號分類", "目錄圖號", "目錄圖名", "實際PDF頁碼", "比對結果", "擷取說明" }) table.Columns.Add(c);
            table.Columns.Add("人工確認", typeof(bool)).DefaultValue = false;
            table.Columns.Add("OCR擷取", typeof(bool)).DefaultValue = true;
            table.Columns.Add("內容分類");
            table.Columns["比對結果"].DefaultValue = "待確認";
            return table;
        }
        internal static bool IsIndex(Page page, IEnumerable<FrameText> frames)
        {
            string heading = SheetFrameReader.Normalize(string.Concat(page.Letters.Where(l => l.BoundingBox.Top > page.Height * 0.85).Select(l => l.Value)));
            return heading.Contains("INDEXOFDRAWINGS") || heading.Contains("圖目錄") || frames.Any(f =>
                SheetFrameReader.Normalize(f.Title).Contains("圖目錄") || f.Title.Split('\r', '\n').Any(l => SheetFrameReader.Normalize(l) == "INDEX"));
        }

        internal static bool HasOcrHeading(string file, Page page, SheetPdfOcr ocr)
        {
            using var json = ocr.Recognize(file, page.Number, new[] { new OcrRegion { Left = 0.25, Top = 0.025, Width = 0.4, Height = 0.065 } }, 10);
            foreach (string source in new[] { "Lines", "EnglishLines", "EnhancedEnglishLines" })
            {
                string text = SheetFrameReader.Normalize(string.Concat(json.RootElement.GetProperty("Cells")[0].GetProperty(source)
                    .EnumerateArray().Select(l => l.GetProperty("Text").GetString())));
                if (text.Contains("圖目錄") || Regex.IsMatch(text, "INDEX[O0]FDRAWINGS")) return true;
            }
            return false;
        }

        internal static List<DrawingIndexEntry> Read(string file, Page page, FrameText frame, SheetPdfOcr ocr,
            IEnumerable<FrameText> hints, Action<string> progress, out string raw)
        {
            double right = frame != null ? frame.TitleCell.Left / page.Width - 0.008 : 0.85;
            right = Math.Max(0.5, Math.Min(right, 0.96));
            var tokens = SheetFrameReader.Pieces(page.Letters).Select(p => new IndexText { Text = p.Text,
                X = p.Box.Left / page.Width, Y = (page.Height - p.Box.Top) / page.Height,
                Width = p.Box.Width / page.Width, Height = p.Box.Height / page.Height }).Where(t => t.X < right).ToList();
            var references = hints.Select(h => h.Number).Where(n => n != null).ToList();
            if (frame?.Number != null) references.Add(frame.Number);
            var entries = Parse(tokens, references, false);
            if (entries.Count >= 2)
            {
                raw = Raw(page.Number, tokens, "PDF 文字層");
                return entries;
            }
            // 窄欄＋分段渲染，避免整張 A1/A3 縮到 Windows OCR 的最大影像尺寸後字體過小。
            tokens.Clear();
            var tiles = new List<OcrRegion>();
            const double left = 0.025, top = 0.075, bottom = 0.975;
            for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++)
            {
                double x = left + (right - left) * column / 4, y = top + (bottom - top) * row / 4;
                tiles.Add(new OcrRegion { Left = Math.Max(0, x - 0.01), Top = Math.Max(0, y - 0.015),
                    Width = Math.Min(1, x + (right - left) / 4 + 0.01) - Math.Max(0, x - 0.01),
                    Height = Math.Min(1, y + (bottom - top) / 4 + 0.015) - Math.Max(0, y - 0.015) });
            }
            var errors = new List<string>();
            for (int i = 0; i < tiles.Count; i++)
            {
                progress?.Invoke($"擷取第 {page.Number} 頁圖目錄 {i + 1}/{tiles.Count} 區\n分欄放大 OCR，保留原文及位置供覆核。");
                try
                {
                    using var json = ocr.Recognize(file, page.Number, new[] { tiles[i] }, 12);
                    var cell = json.RootElement.GetProperty("Cells")[0];
                    int variant = 0;
                    foreach (string property in new[] { "Lines", "EnglishLines", "EnhancedEnglishLines", "EnhancedLines" })
                    {
                        foreach (var line in cell.GetProperty(property).EnumerateArray())
                        {
                            var words = line.GetProperty("Words").EnumerateArray().ToList();
                            if (words.Count == 0) continue;
                            double x = words.Min(w => w.GetProperty("X").GetDouble()), y = words.Min(w => w.GetProperty("Y").GetDouble());
                            double r = words.Max(w => w.GetProperty("X").GetDouble() + w.GetProperty("Width").GetDouble());
                            double b = words.Max(w => w.GetProperty("Y").GetDouble() + w.GetProperty("Height").GetDouble());
                            tokens.Add(new IndexText { Text = line.GetProperty("Text").GetString(), X = tiles[i].Left + x * tiles[i].Width,
                                Y = tiles[i].Top + y * tiles[i].Height, Width = (r - x) * tiles[i].Width,
                                Height = (b - y) * tiles[i].Height, Variant = variant });
                        }
                        variant++;
                    }
                }
                catch (Exception ex) { errors.Add($"第 {i + 1} 區：{ex.Message}"); }
            }
            entries = Parse(tokens, references, true);
            raw = Raw(page.Number, tokens, "分欄放大 OCR（不可視為已完整擷取，需對照原圖覆核）") +
                Environment.NewLine + string.Join(Environment.NewLine, errors);
            foreach (var entry in entries) entry.Note += errors.Count > 0 ? "；部分區域辨識失敗" : "；請核對目錄是否有漏列";
            return entries;
        }

        private static string Raw(int page, List<IndexText> tokens, string source) => $"PDF 第 {page} 頁：{source}" + Environment.NewLine +
            string.Join(Environment.NewLine, tokens.OrderBy(t => Math.Round(t.X / 0.2)).ThenBy(t => t.Y)
                .Select(t => $"({t.X:F3}, {t.Y:F3}) [{t.Variant}] {t.Text}"));

        internal static List<DrawingIndexEntry> Parse(List<IndexText> tokens, IEnumerable<string> references, bool ocr)
        {
            // OCR 常把 SB180- 與 AR0111 拆成兩個片段；先按位置接回，不能靠猜數字補列。
            var joined = new List<IndexText>(tokens);
            foreach (var token in tokens)
            {
                var neighbors = tokens.Where(t => t != token && t.Variant == token.Variant &&
                    Math.Abs(t.Y - token.Y) < Math.Max(0.002, token.Height * 0.6) &&
                    t.X >= token.X + token.Width - 0.001 && t.X - token.X - token.Width < 0.008).OrderBy(t => t.X).ToList();
                if (neighbors.Count > 0)
                {
                    var next = neighbors[0];
                    joined.Add(new IndexText { Text = token.Text + " " + next.Text, X = token.X, Y = Math.Min(token.Y, next.Y),
                        Width = next.X + next.Width - token.X, Height = Math.Max(token.Height, next.Height), Variant = token.Variant });
                }
            }
            // 圖號只作為目錄列的錨點；絕不當作該圖紙已實際存在的證據。
            var anchors = new List<(IndexText Token, string Number)>();
            foreach (var token in joined)
            {
                string number = Number(token.Text, references, ocr);
                if (number == null) continue;
                var duplicate = anchors.FindIndex(a => Math.Abs(a.Token.X - token.X) < 0.03 && Math.Abs(a.Token.Y - token.Y) < Math.Max(0.004, token.Height * 0.7));
                if (duplicate < 0) anchors.Add((token, number));
                else if (token.Variant > anchors[duplicate].Token.Variant) anchors[duplicate] = (token, number);
            }
            var columns = new List<List<(IndexText Token, string Number)>>();
            foreach (var anchor in anchors.OrderBy(a => a.Token.X))
            {
                var column = columns.FirstOrDefault(c => Math.Abs(c.Average(a => a.Token.X) - anchor.Token.X) < 0.04);
                if (column == null) { column = new List<(IndexText, string)>(); columns.Add(column); }
                column.Add(anchor);
            }
            var result = new List<DrawingIndexEntry>();
            for (int c = 0; c < columns.Count; c++)
            {
                var column = columns[c].OrderBy(a => a.Token.Y).ToList();
                double left = column.Min(a => a.Token.X) - 0.008;
                double right = c + 1 < columns.Count ? columns[c + 1].Min(a => a.Token.X) - 0.008 : 0.86;
                for (int row = 0; row < column.Count; row++)
                {
                    var anchor = column[row];
                    double bottom = row + 1 < column.Count ? column[row + 1].Token.Y - 0.008 : Math.Min(0.97, anchor.Token.Y + 0.085);
                    var titleLines = tokens.Where(t => t.X >= left && t.X < right && t.Y >= anchor.Token.Y - 0.008 && t.Y < bottom)
                        .OrderBy(t => t.Y).ThenBy(t => t.X).ToList();
                    var chosen = new List<IndexText>();
                    foreach (var line in titleLines)
                    {
                        // 同位置的中／英文／增強辨識只選一份，分段重疊區也以位置去重。
                        var existing = chosen.FindIndex(t => Math.Abs(t.Y - line.Y) < Math.Max(0.002, Math.Min(t.Height, line.Height) * 0.5) &&
                            Math.Abs(t.X - line.X) < 0.04);
                        int Score(IndexText t) => Regex.IsMatch(t.Text, @"[\u4e00-\u9fff]") && (t.Variant == 0 || t.Variant == 3) ?
                            100 + Regex.Matches(t.Text, @"[\u4e00-\u9fff]").Count : t.Variant == 3 ? 0 : t.Variant * 10;
                        if (existing < 0) chosen.Add(line);
                        else if (Score(line) > Score(chosen[existing])) chosen[existing] = line;
                    }
                    var names = new List<string>();
                    foreach (var text in chosen.OrderBy(t => t.Y).ThenBy(t => t.X))
                    {
                        string value = text.Text;
                        string n = Number(value, references, ocr);
                        if (n != null || text.X < anchor.Token.X + 0.04)
                        {
                            // 圖號與第一行圖名有時被 OCR 合併成一行；保留圖號後的內容。
                            var match = NumberPattern.Match(SheetFrameReader.Normalize(value));
                            value = match.Success ? SheetFrameReader.Normalize(value).Substring(match.Index + match.Length) : "";
                        }
                        if (!string.IsNullOrWhiteSpace(value) && !Regex.IsMatch(value.Trim(), @"^[-—–·:：]+$")) names.Add(value.Trim());
                    }
                    result.Add(new DrawingIndexEntry { Number = anchor.Number, Title = string.Join(Environment.NewLine, names),
                        Ocr = ocr, Note = ocr ? "目錄分欄 OCR，需覆核圖號／圖名" : "PDF 文字層，依圖號位置分欄分列" });
                }
            }
            return result;
        }
        private static readonly Regex NumberPattern = new Regex(@"[A-Z]{2,5}[0-9OIL]{2,4}[-一][A-Z0-9]{2,4}[0-9OIL]{3,5}");
        internal static string Number(string text, IEnumerable<string> references, bool ocr)
        {
            var match = NumberPattern.Match(SheetFrameReader.Normalize(text));
            if (!match.Success) return null;
            string candidate = match.Value;
            return ocr ? SheetOcrNumber.Repair(candidate, references) : Regex.IsMatch(candidate, @"^[A-Z]+\d+-[A-Z]+\d+$") ? candidate : null;
        }
        internal static void Add(DataTable table, int page, IEnumerable<DrawingIndexEntry> entries)
        {
            foreach (var entry in entries)
            {
                var code = Regex.Match(entry.Number, @"^([A-Z]+\d+)-([A-Z]+)");
                table.Rows.Add(page, code.Success ? code.Groups[1].Value + " / " + code.Groups[2].Value : "", entry.Number,
                    entry.Title, "", "待比對", entry.Note, false, entry.Ocr, Category(entry.Title));
            }
        }
        internal static string Category(string title)
        {
            string t = SheetFrameReader.Normalize(title);
            if (Regex.IsMatch(t, "圖目錄|INDEX")) return "圖目錄";
            if (Regex.IsMatch(t, "一般說明|縮寫|符號|材料|GENERALNOTES|ABBREVIATION|SYMBOL|MATERIAL")) return "一般說明／材料符號";
            if (Regex.IsMatch(t, "剖面|SECTION")) return "剖面圖";
            if (Regex.IsMatch(t, "立面|ELEVATION")) return "立面圖";
            if (Regex.IsMatch(t, "天花|RCP|CEILING")) return "天花平面圖";
            if (Regex.IsMatch(t, "平面|配置|PLAN|LAYOUT")) return "平面／配置圖";
            if (Regex.IsMatch(t, "詳圖|DETAIL")) return "詳圖";
            return string.IsNullOrWhiteSpace(t) ? "待確認" : "其他";
        }
        internal static void Compare(DataTable index, List<(int Page, FrameText Frame)> actual, DataTable pdfRows)
        {
            var reviewed = pdfRows.AsEnumerable().Where(r => r.Field<bool>("人工確認")).ToList();
            var reviewedPages = new HashSet<int>(reviewed.Select(r => int.Parse(r.Field<string>("PDF頁碼"))));
            bool incomplete = pdfRows.AsEnumerable().Any(r => !r.Field<bool>("人工確認") && r.Field<string>("辨識結果").StartsWith("待確認"));
            var proof = actual.Where(a => !reviewedPages.Contains(a.Page) && !a.Frame.OcrCandidate).ToList();
            foreach (var r in reviewed.Where(r => !string.IsNullOrWhiteSpace(r.Field<string>("圖號"))))
                proof.Add((int.Parse(r.Field<string>("PDF頁碼")), new FrameText { Number = r.Field<string>("圖號"), Title = r.Field<string>("圖名") }));
            foreach (DataRow row in index.Rows)
            {
                string number = row.Field<string>("目錄圖號"), title = row.Field<string>("目錄圖名");
                row["內容分類"] = Category(title);
                bool confirmed = row.Field<bool>("人工確認");
                row["實際PDF頁碼"] = "";
                if (string.IsNullOrWhiteSpace(number)) { row["比對結果"] = confirmed ? "已排除" : "待確認：目錄未完整擷取"; continue; }
                if (string.IsNullOrWhiteSpace(title)) { row["比對結果"] = "待確認：目錄圖名為空白"; continue; }
                var category = Regex.Match(number, @"^([A-Z]+\d+)-([A-Z]+)");
                row["圖號分類"] = category.Success ? category.Groups[1].Value + " / " + category.Groups[2].Value : "";
                var found = proof.Where(p => SheetFrameReader.Normalize(p.Frame.Number) == SheetFrameReader.Normalize(number)).ToList();
                var candidates = actual.Where(p => p.Frame.OcrCandidate && SheetFrameReader.Normalize(p.Frame.Number) == SheetFrameReader.Normalize(number)).ToList();
                row["實際PDF頁碼"] = string.Join(", ", found.Concat(candidates).Select(p => p.Page).Distinct().OrderBy(n => n));
                if (row.Field<bool>("OCR擷取") && !confirmed) row["比對結果"] = found.Count > 0 || candidates.Count > 0 ? "找到候選頁／目錄待確認" : "未找到／目錄待確認";
                else if (found.Count > 0) row["比對結果"] = found.Any(p => SheetFrameReader.Normalize(p.Frame.Title) == SheetFrameReader.Normalize(title)) ? "已收錄" : "已找到圖號／圖名不一致";
                else row["比對結果"] = incomplete || candidates.Count > 0 ? "未能確認／PDF待確認" : "目錄有列／PDF缺頁";
            }
        }
    }
}
