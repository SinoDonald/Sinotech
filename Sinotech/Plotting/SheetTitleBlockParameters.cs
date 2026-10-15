using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Sinotech.Plotting
{
    /// <summary>
    /// 每種圖框只建立一次「只有圖框的暫存圖紙」。把可寫文字參數替換成唯一代碼，
    /// 由一次 PDF 判斷圖名／圖號实际使用的參數及前後綴；之後不再逐張出圖。
    /// 整個校準位於 TransactionGroup，無論成功失敗都 RollBack，恢復專案與類型參數。
    /// </summary>
    internal static class SheetTitleBlockParameters
    {
        internal static string CacheKey(FamilyInstance block) => block.Symbol.UniqueId + "|" +
            string.Join(";", block.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.Integer)
                .OrderBy(p => p.Id.Value).Select(p => p.Id.Value + "=" + p.AsInteger()));

        internal static TitleBlockRecipe Discover(Document doc, ViewSheet original, FamilyInstance originalBlock, string folder)
        {
            using var group = new TransactionGroup(doc, "暫時辨識圖框參數");
            group.Start();
            string path = Path.Combine(folder, "parameter-probe.pdf");
            try
            {
                ViewSheet probe;
                var bindings = new Dictionary<string, ParameterBinding>();
                using (var tx = new Transaction(doc, "建立只有圖框的參數探針圖紙"))
                {
                    tx.Start();
                    var handling = tx.GetFailureHandlingOptions().SetFailuresPreprocessor(new ProbeFailures()).SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(handling);
                    probe = ViewSheet.Create(doc, originalBlock.Symbol.Id);
                    var block = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .OfType<FamilyInstance>().Single(b => b.OwnerViewId == probe.Id);
                    CopyValues(original, probe);
                    CopyValues(originalBlock, block);
                    int index = 0;
                    foreach (var source in new[] { ("Sheet", (Element)probe), ("TitleBlock", (Element)block),
                        ("Type", (Element)block.Symbol), ("Project", (Element)doc.ProjectInformation) })
                    {
                        foreach (Parameter p in source.Item2.Parameters.Cast<Parameter>().OrderBy(p => p.Id.Value))
                        {
                            if (p.IsReadOnly || p.StorageType != StorageType.String ||
                                p.Id.Value == (long)BuiltInParameter.ALL_MODEL_TYPE_NAME ||
                                p.Id.Value == (long)BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM) continue;
                            string token = "ZX" + (++index).ToString("D4") + "XZ";
                            var binding = new ParameterBinding { Source = source.Item1, Id = p.Id.Value, Name = p.Definition.Name,
                                Guid = p.IsShared ? p.GUID : (Guid?)null };
                            try { if (p.Set(token)) bindings.Add(token, binding); }
                            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
                        }
                    }
                    if (tx.Commit() != TransactionStatus.Committed)
                        throw new InvalidOperationException("圖框參數校準交易失敗；沒有修改原圖紙。");
                }
                using var options = new PDFExportOptions { Combine = true, FileName = "parameter-probe",
                    ColorDepth = ColorDepthType.BlackLine, ExportQuality = PDFExportQualityType.DPI300 };
                // Revit 2024 的 PDF 匯出為同步呼叫，沒有背景匯出選項。
                if (!doc.Export(folder, new List<ElementId> { probe.Id }, options))
                    throw new InvalidOperationException("圖框參數校準 PDF 輸出失敗。");
                using var pdf = PdfDocument.Open(path);
                var frames = SheetFrameReader.Read(pdf.GetPage(1), out string why);
                if (frames.Count != 1) throw new InvalidOperationException("無法校準圖框參數：" + why);
                var recipe = new TitleBlockRecipe { Pattern = frames[0], Bindings = bindings };
                if (!bindings.Keys.Any(k => SheetFrameReader.Normalize(recipe.Pattern.Number).Contains(k)))
                    throw new InvalidOperationException("圖號沒有可追蹤的文字參數，需另行設定圖框參數組合。");
                return recipe;
            }
            finally
            {
                if (group.GetStatus() == TransactionStatus.Started && group.RollBack() != TransactionStatus.RolledBack)
                    throw new InvalidOperationException("圖框校準無法復原，請立即檢查專案交易狀態。");
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static void CopyValues(Element from, Element to)
        {
            foreach (Parameter p in from.Parameters)
            {
                if (p.Id.Value == (long)BuiltInParameter.SHEET_NUMBER) continue;
                Parameter q = to.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Id == p.Id);
                if (q == null || q.IsReadOnly || !p.HasValue || q.StorageType != p.StorageType) continue;
                try
                {
                    switch (p.StorageType)
                    {
                        case StorageType.String: q.Set(p.AsString() ?? ""); break;
                        case StorageType.Integer: q.Set(p.AsInteger()); break;
                        case StorageType.Double: q.Set(p.AsDouble()); break;
                        case StorageType.ElementId: q.Set(p.AsElementId()); break;
                    }
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException) { }
            }
        }

        private sealed class ProbeFailures : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (var failure in accessor.GetFailureMessages())
                    if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
                return accessor.GetFailureMessages().Any(f => f.GetSeverity() == FailureSeverity.Error)
                    ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }
    }

    internal sealed class ParameterBinding
    {
        internal string Source, Name;
        internal long Id;
        internal Guid? Guid;
        internal string Read(Document doc, ViewSheet sheet, FamilyInstance block)
        {
            Element element = Source == "Sheet" ? (Element)sheet : Source == "TitleBlock" ? (Element)block : Source == "Type" ? (Element)block.Symbol : doc.ProjectInformation;
            Parameter p = Guid.HasValue ? element.get_Parameter(Guid.Value) :
                Id < 0 ? element.get_Parameter((BuiltInParameter)Id) : element.Parameters.Cast<Parameter>().FirstOrDefault(x => x.Id.Value == Id);
            if (p == null) throw new InvalidOperationException("找不到圖框參數：" + Source + "." + Name);
            return p.AsString() ?? "";
        }
    }

    internal sealed class TitleBlockRecipe
    {
        internal FrameText Pattern;
        internal Dictionary<string, ParameterBinding> Bindings;
        internal FrameText Read(Document doc, ViewSheet sheet, FamilyInstance block)
        {
            string Render(string template) => SheetParameterText.Substitute(template,
                Bindings.Where(kv => SheetFrameReader.Normalize(template).Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Read(doc, sheet, block)));
            return new FrameText { Title = Render(Pattern.Title), Number = Render(Pattern.Number),
                TitleCell = Pattern.TitleCell, NumberCell = Pattern.NumberCell, PageWidth = Pattern.PageWidth, PageHeight = Pattern.PageHeight };
        }
        internal string Description => "圖名：" + Describe(Pattern.Title) + "；圖號：" + Describe(Pattern.Number);
        private string Describe(string text) => string.Join(" + ", Bindings.Where(b => SheetFrameReader.Normalize(text).Contains(b.Key))
            .OrderBy(b => SheetFrameReader.Normalize(text).IndexOf(b.Key, StringComparison.Ordinal))
            .Select(b => b.Value.Source + "." + b.Value.Name));
    }

    internal static class SheetParameterText
    {
        // 先辨識所有代碼再替換，參數值若剛好包含其他代碼也不會被二次替換。
        internal static string Substitute(string template, Dictionary<string, string> values)
        {
            string result = Regex.Replace(template, @"Z\s*X\s*\d\s*\d\s*\d\s*\d\s*X\s*Z", match =>
            {
                string token = SheetFrameReader.Normalize(match.Value);
                if (!values.TryGetValue(token, out string value)) throw new InvalidOperationException("無法解析圖框參數代碼：" + token);
                return value;
            });
            return result.Trim();
        }
    }
}
