using System;
using Autodesk.Revit.DB;
class Check {
        internal static ExportPaperFormat ResolvePdfPaperFormat(string paperName)
        {
            // 僅辨識完整紙型名稱或驅動程式附加的尺寸說明，避免 A1 誤判成 A10/A1+。
            string name = (paperName ?? "").Trim().ToUpperInvariant();
            foreach (ExportPaperFormat format in Enum.GetValues(typeof(ExportPaperFormat)))
            {
                if (format == ExportPaperFormat.Default) continue;
                string fullName = format.ToString().Replace('_', ' ').ToUpperInvariant();
                string shortName = fullName.StartsWith("ISO ") ? fullName.Substring(4) : fullName;
                foreach (string candidate in new[] { fullName, shortName })
                {
                    if (name == candidate || name.StartsWith(candidate + " (", StringComparison.Ordinal)
                        || name.StartsWith(candidate + "(", StringComparison.Ordinal)) return format;
                }
            }
            // 常見 Windows 印表機的北美紙型別名。
            switch (name)
            {
                case "LETTER": return ExportPaperFormat.ANSI_A;
                case "TABLOID": return ExportPaperFormat.ANSI_B;
            }
            throw new NotSupportedException("列印紙張「" + paperName
                + "」無法對應至原生 PDF 紙張。請選用標準紙型（例如 A1），不會自動改用內容範圍匯出。");
        }


static void Main() {
foreach (ExportPaperFormat format in Enum.GetValues(typeof(ExportPaperFormat))) {
if (format == ExportPaperFormat.Default) continue;
string name = format.ToString().Replace('_',' ');
if (ResolvePdfPaperFormat(name) != format) throw new Exception(name);
if (name.StartsWith("ISO ") && ResolvePdfPaperFormat(name.Substring(4) + " (594 x 841 mm)") != format) throw new Exception(name);
}
if (ResolvePdfPaperFormat("A1") != ExportPaperFormat.ISO_A1) throw new Exception("A1");
foreach (string name in new[] { "A10", "A1+", "Custom", "", "A1 oversized" }) {
bool rejected = false; try { ResolvePdfPaperFormat(name); } catch (NotSupportedException) { rejected = true; }
if (!rejected) throw new Exception("Unexpected paper: " + name);
}
Console.WriteLine("PASS: standard paper names, driver size suffixes, and unsupported paper rejection.");
}}