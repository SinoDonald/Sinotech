using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace Sinotech_2025.Plotting
{
    /// <summary>只對無法文字擷取的頁面，依模型欄格位置做局部 OCR；結果需人工確認。</summary>
    internal sealed class SheetPdfOcr : IDisposable
    {
        private Process worker;
        private string startupFailure;
        private readonly string folder;
        internal string Diagnostics { get; private set; }
        internal SheetPdfOcr(string folder) { this.folder = folder; }

        internal List<FrameText> Read(string file, Page page, IEnumerable<FrameText> hints, out string reason)
        {
            reason = "";
            var results = new List<FrameText>();
            Diagnostics = "";
            var vocabulary = new HashSet<string>(hints.SelectMany(h => Regex.Matches(h.Title ?? "", @"[A-Za-z]{3,}").Cast<Match>()
                .Select(m => m.Value.ToUpperInvariant())).Concat(new[] { "INDEX", "STATION" }));
            var layouts = new List<FrameText>();
            foreach (var hint in hints.Where(f => f.PageWidth > 0 && f.PageHeight > 0))
                if (!layouts.Any(f => f.SameLayout(hint))) layouts.Add(hint);
            if (layouts.Count == 0) { reason = "沒有可用的圖框欄格樣式供局部 OCR。"; return results; }
            try
            {
                EnsureWorker();
                foreach (var hint in layouts.Take(12))
                {
                    var cells = SheetFrameReader.ProjectCells(page, hint, out string geometry);
                    Diagnostics += geometry;
                    if (!cells.HasValue) { Diagnostics += "找不到對應封閉欄格；"; continue; }
                    var title = cells.Value.Title; var number = cells.Value.Number;
                    foreach (double scale in new[] { 6.0, 10.0, 16.0 })
                    {
                        using var json = Recognize(file, page.Number, new[] { OcrRegion.From(title, page), OcrRegion.From(number, page) }, scale);
                        var extracted = json.RootElement.GetProperty("Cells");
                        if (extracted.GetArrayLength() != 2) continue;
                        string t = CellText(extracted[0], true, out bool titleLabel);
                        string n = CellText(extracted[1], false, out bool numberLabel);
                        var alternate = CellText(extracted[1], false, out _, "EnhancedEnglishLines");
                        string improved = SheetOcrNumber.Repair(alternate, hints.Select(f => f.Number));
                        if (improved != null) n = improved;
                        else n = SheetOcrNumber.Repair(n, hints.Select(f => f.Number)) ?? n;
                        Diagnostics += "OCR 原文圖號：" + CellText(extracted[1], false, out _) + "；候選圖號：" + n;
                        // 需同時有欄格邊框、圖名標題與圖號標題，不能只讀到圖面索引號就當成圖框。
                        if (!titleLabel || !numberLabel || string.IsNullOrWhiteSpace(t) || !Regex.IsMatch(n, @"\d") ||
                            !Regex.IsMatch(n, @"[A-Za-z]")) continue;
                        var frame = new FrameText { Title = t, Number = n, TitleCell = title, NumberCell = number,
                            PageWidth = page.Width, PageHeight = page.Height, OcrCandidate = true, OcrScale = scale };
                        int Score(FrameText f)
                        {
                            int score = SheetOcrNumber.Repair(f.Number, hints.Select(h => h.Number)) != null ? 100 : 0;
                            foreach (string line in f.Title.Split('\r', '\n').Where(l => l.Length > 0))
                            {
                                int chinese = Regex.Matches(line, @"[\u4e00-\u9fff]").Count;
                                int latin = Regex.Matches(line, @"[A-Za-z]").Count;
                                score += Math.Min(chinese, 20) * 2;
                                if (chinese > 0 && latin > chinese * 2) score -= 15;
                                score += Regex.Matches(line, @"[A-Za-z]{3,}").Cast<Match>().Count(m => vocabulary.Contains(m.Value.ToUpperInvariant())) * 5;
                            }
                            return score - Regex.Matches(f.Title, @"[\\<>]").Count * 10;
                        }
                        var previous = results.FirstOrDefault(f => Math.Abs(f.TitleCell.Left - frame.TitleCell.Left) < 1);
                        if (previous == null || Score(frame) > Score(previous)) { if (previous != null) results.Remove(previous); results.Add(frame); }
                        if (Score(frame) >= 100 && !Regex.IsMatch(t, @"[\\<>]")) break;
                    }
                }
                if (results.Count == 0) reason = "局部 OCR 未能同時確認圖名／圖號標題及完整內容，請人工確認。";
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                // 逾時後不能沿用同一 stdin/stdout 協定，否則下一頁可能讀到前一頁的延遲回覆。
                if (ex is TimeoutException || ex is JsonException || ex is IOException || worker != null && worker.HasExited)
                {
                    startupFailure = reason;
                    Dispose();
                }
            }
            return results;
        }

        internal JsonDocument Recognize(string file, int page, IEnumerable<OcrRegion> regions, double scale)
        {
            EnsureWorker();
            try
            {
                worker.StandardInput.WriteLine(JsonSerializer.Serialize(new { Path = Path.GetFullPath(file), Page = page, Regions = regions, Scale = scale }));
                worker.StandardInput.Flush();
                var response = worker.StandardOutput.ReadLineAsync();
                if (!response.Wait(TimeSpan.FromSeconds(45))) throw new TimeoutException("Windows 局部 OCR 逾時。");
                if (response.Result == null) { worker.WaitForExit(1000); throw new IOException(startupFailure ?? "Windows OCR 程序已結束。"); }
                var json = JsonDocument.Parse(response.Result);
                string error = json.RootElement.GetProperty("Error").GetString();
                if (!string.IsNullOrEmpty(error)) { json.Dispose(); throw new InvalidOperationException(error); }
                return json;
            }
            catch (Exception ex) when (ex is TimeoutException || ex is JsonException || ex is IOException)
            {
                startupFailure = ex.Message; Dispose(); throw;
            }
        }

        private static string CellText(JsonElement cell, bool title, out bool hasLabel, string englishProperty = "EnglishLines")
        {
            hasLabel = false;
            var lines = new List<string>();
            var chosen = title ? cell.GetProperty("Lines").EnumerateArray().ToList() : cell.GetProperty(englishProperty).EnumerateArray().ToList();
            if (title)
            {
                var english = cell.GetProperty("EnglishLines").EnumerateArray().ToList();
                for (int i = 0; i < chosen.Count; i++)
                {
                    var words = chosen[i].GetProperty("Words").EnumerateArray().ToList();
                    string raw = chosen[i].GetProperty("Text").GetString();
                    int chinese = Regex.Matches(raw, @"[\u4e00-\u9fff]").Count;
                    int latin = Regex.Matches(raw, @"[A-Za-z]").Count;
                    if (words.Count == 0 || chinese > 0 && latin <= chinese * 2) continue;
                    double y = words.Min(w => w.GetProperty("Y").GetDouble());
                    var matching = english.Where(l => l.GetProperty("Words").GetArrayLength() > 0)
                        .OrderBy(l => Math.Abs(l.GetProperty("Words").EnumerateArray().Min(w => w.GetProperty("Y").GetDouble()) - y)).ToList();
                    if (matching.Count > 0 && Math.Abs(matching[0].GetProperty("Words").EnumerateArray().Min(w => w.GetProperty("Y").GetDouble()) - y) < 0.08)
                        chosen[i] = matching[0];
                }
            }
            foreach (var line in chosen)
            {
                var content = new List<string>();
                var label = new StringBuilder();
                foreach (var word in line.GetProperty("Words").EnumerateArray())
                {
                    string text = word.GetProperty("Text").GetString();
                    double x = word.GetProperty("X").GetDouble();
                    string key = Regex.Replace(SheetFrameReader.Normalize(text), @"[\p{P}]", "");
                    bool isLabel = x < 0.24 && (title ? new[] { "TITLE", "TLTLE", "TTLE", "圖名", "圖", "名", "图名", "图" }.Contains(key) :
                        new[] { "DWG", "NO", "DWGNO", "圖號", "圖", "號", "图号", "图", "号" }.Contains(key));
                    if (isLabel) label.Append(key); else if (!(x < 0.24 && key.Length == 0)) content.Add(text);
                }
                string labelKey = label.ToString();
                if (title ? labelKey.Contains("TITLE") || labelKey.Contains("圖名") : labelKey.Contains("DWG") || labelKey.Contains("圖號")) hasLabel = true;
                if (content.Count > 0) lines.Add(string.Join(" ", content));
            }
            return string.Join(Environment.NewLine, lines);
        }

        private void EnsureWorker()
        {
            if (startupFailure != null) throw new InvalidOperationException(startupFailure);
            if (worker != null) return;
            try
            {
                string script = Path.Combine(folder, "SheetPdfOcr.ps1");
                using var resource = typeof(SheetPdfOcr).Assembly.GetManifestResourceStream("Sinotech_2025.Plotting.SheetPdfOcr.ps1");
                if (resource == null) throw new InvalidOperationException("缺少內嵌 OCR 元件，請重建 Sinotech_2025。");
                using (var reader = new StreamReader(resource, Encoding.UTF8)) File.WriteAllText(script, reader.ReadToEnd(), new UTF8Encoding(true));
                var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
                foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script }) info.ArgumentList.Add(argument);
                worker = Process.Start(info);
                worker.ErrorDataReceived += (_, e) => { if (e.Data != null) startupFailure = "Windows OCR 啟動錯誤：" + e.Data; };
                worker.BeginErrorReadLine();
            }
            catch (Exception ex) { startupFailure = ex.Message; throw; }
        }

        public void Dispose()
        {
            if (worker == null) return;
            try { worker.StandardInput.Close(); if (!worker.WaitForExit(2000)) worker.Kill(true); }
            finally { worker.Dispose(); worker = null; }
        }
    }

    internal sealed class OcrRegion
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        internal static OcrRegion From(PdfRectangle box, Page page) => new OcrRegion { Left = box.Left / page.Width,
            Top = (page.Height - box.Top) / page.Height, Width = box.Width / page.Width, Height = box.Height / page.Height };
    }

    internal static class SheetOcrNumber
    {
        // 只對待人工覆核的 OCR 候選，依已讀圖號的字母／數字位置修正常見混淆字；不據此自動判定匯出。
        internal static string Repair(string text, IEnumerable<string> references)
        {
            var masks = references.Select(n => Regex.Match(SheetFrameReader.Normalize(n), @"^([A-Z]+)(\d+)-([A-Z]+)(\d+)$"))
                .Where(m => m.Success).Select(m => (m.Groups[1].Length, m.Groups[2].Length, m.Groups[3].Length, m.Groups[4].Length)).Distinct().ToList();
            string candidate = SheetFrameReader.Normalize(text).Replace('一', '-').Replace('○', '0');
            foreach (var mask in masks)
            {
                int leftLength = mask.Item1 + mask.Item2, rightLength = mask.Item3 + mask.Item4;
                if (candidate.Length != leftLength + rightLength + 1 || candidate[leftLength] != '-') continue;
                char[] letters = candidate.ToCharArray(); bool valid = true;
                for (int i = 0; i < letters.Length; i++)
                {
                    if (i == leftLength) continue;
                    bool numeric = i >= mask.Item1 && i < leftLength || i >= leftLength + 1 + mask.Item3;
                    char c = letters[i];
                    if (numeric && !char.IsDigit(c)) c = c == 'O' || c == 'Q' ? '0' : c == 'I' || c == 'L' ? '1' : c == 'S' ? '5' : c == 'B' ? '8' : c;
                    if (!numeric && !char.IsLetter(c)) c = c == '0' ? 'O' : c == '1' ? 'I' : c == '6' ? 'G' : c == '5' ? 'S' : c == '8' ? 'B' : c;
                    if (numeric ? c < '0' || c > '9' : c < 'A' || c > 'Z') { valid = false; break; }
                    letters[i] = c;
                }
                if (valid) return new string(letters);
            }
            return null;
        }
    }
}
