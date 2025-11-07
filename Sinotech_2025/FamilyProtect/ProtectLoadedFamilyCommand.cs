using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.IO;
using System.Security.Cryptography;

namespace Sinotech_2025.FamilyProtect
{
    [Transaction(TransactionMode.Manual)]
    public class ProtectLoadedFamilyCommand : IExternalCommand
    {
        // --- Patch configuration (adjustable) ---
        private class PatchRegion
        {
            public long Offset;
            public int Length;
            public PatchMode Mode;
            public byte Key;
        }

        private enum PatchMode { XOR, RandomOverwrite, ZeroOverwrite, AppendSignature }

        // 保守的預設 patch（建議先用此測試）
        private static PatchRegion[] ConservativePatches = new PatchRegion[]
        {
            new PatchRegion { Offset = 4 * 1024, Length = 256, Mode = PatchMode.XOR, Key = 0x7A },
            new PatchRegion { Offset = 16 * 1024, Length = 256, Mode = PatchMode.RandomOverwrite, Key = 0x00 },
            new PatchRegion { Offset = -1, Length = 128, Mode = PatchMode.AppendSignature, Key = 0x00 }
        };

        // 攻擊性較強（風險較高，測試用）
        private static PatchRegion[] AggressivePatches = new PatchRegion[]
        {
            new PatchRegion { Offset = 4 * 1024, Length = 512, Mode = PatchMode.XOR, Key = 0x7A },
            new PatchRegion { Offset = 32 * 1024, Length = 1024, Mode = PatchMode.RandomOverwrite, Key = 0x00 },
            new PatchRegion { Offset = 128 * 1024, Length = 2048, Mode = PatchMode.XOR, Key = 0xB6 },
            new PatchRegion { Offset = -1, Length = 256, Mode = PatchMode.AppendSignature, Key = 0x00 }
        };

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            Document doc = uidoc.Document;

            try
            {
                // 選取族實例
                Reference r = null;
                try
                {
                    r = uidoc.Selection.PickObject(ObjectType.Element, new FamilyInstanceSelectionFilter(), "請選擇一個族實例 (FamilyInstance)");
                }
                catch (OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                if (r == null)
                {
                    TaskDialog.Show("保護族", "未選取任何元素。");
                    return Result.Cancelled;
                }

                FamilyInstance fi = doc.GetElement(r) as FamilyInstance;
                if (fi == null)
                {
                    TaskDialog.Show("保護族", "選取的不是族實例。");
                    return Result.Cancelled;
                }

                Family family = fi.Symbol.Family;
                if (family == null)
                {
                    TaskDialog.Show("保護族", "無法取得該族的 Family。");
                    return Result.Failed;
                }

                // 匯出 family 為暫存 rfa
                string tempDir = Path.Combine(Path.GetTempPath(), "RfaProtection");
                Directory.CreateDirectory(tempDir);

                string exportRfa = Path.Combine(tempDir, $"{SanitizeFileName(family.Name)}_{Guid.NewGuid():N}.rfa");
                string protectedRfa = Path.Combine(tempDir, $"{SanitizeFileName(family.Name)}_{Guid.NewGuid():N}_protected.rfa");

                // EditFamily -> SaveAs -> Close family doc
                Document famDoc = null;
                try
                {
                    famDoc = doc.EditFamily(family);
                    if (famDoc == null)
                    {
                        TaskDialog.Show("保護族", "無法開啟 FamilyDocument 以匯出。");
                        return Result.Failed;
                    }

                    // SaveAs (overwrite if exists)
                    var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
                    famDoc.SaveAs(exportRfa, saveOptions);
                    famDoc.Close(false);
                }
                catch (Exception ex)
                {
                    // 若 famDoc 尚開，嘗試關閉
                    try { famDoc?.Close(false); } catch { }
                    TaskDialog.Show("保護族", $"匯出族檔失敗：{ex.Message}");
                    return Result.Failed;
                }

                // 讓使用者選擇保守或 agressive 模式
                var modeResult = TaskDialog.Show("選擇模式", "選擇保護模式：\nYes = Conservative（建議）\nNo = Aggressive（高風險）\nCancel = 取消",
                    TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No | TaskDialogCommonButtons.Cancel);

                if (modeResult == TaskDialogResult.Cancel)
                {
                    TrySecureDelete(exportRfa);
                    return Result.Cancelled;
                }

                var patches = (modeResult == TaskDialogResult.Yes) ? ConservativePatches : AggressivePatches;

                // 執行破壞性 patch（寫入 protectedRfa）
                try
                {
                    File.Copy(exportRfa, protectedRfa, true);
                    ApplyPatchesToFile(protectedRfa, patches);
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("保護族", $"處理暫存檔時發生錯誤：{ex.Message}");
                    TrySecureDelete(exportRfa);
                    TrySecureDelete(protectedRfa);
                    return Result.Failed;
                }

                // 載入 protected rfa 回專案（覆蓋原族）
                Family loadedFamily = null;
                try
                {
                    using (Transaction tx = new Transaction(doc, "Load Protected Family"))
                    {
                        tx.Start();
                        bool loaded = doc.LoadFamily(protectedRfa, new OverwriteFamilyLoadOptions(), out loadedFamily);
                        tx.Commit();

                        if (!loaded)
                        {
                            TaskDialog.Show("保護族", "Revit 無法載入處理後的 .rfa。請改用更保守的模式或檢查備份。");
                            TrySecureDelete(exportRfa);
                            TrySecureDelete(protectedRfa);
                            return Result.Failed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("保護族", $"載入族檔時發生錯誤：{ex.Message}");
                    TrySecureDelete(exportRfa);
                    TrySecureDelete(protectedRfa);
                    return Result.Failed;
                }

                // 載入成功 → 嘗試覆寫並刪除暫存檔（降低殘留）
                TrySecureDelete(exportRfa);
                TrySecureDelete(protectedRfa);

                TaskDialog.Show("保護族", $"族 '{loadedFamily?.Name ?? family.Name}' 已載入為保護版。\n請測試「編輯族群」功能是否被阻止。");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }

        // --- 工具函式 ---

        private static void ApplyPatchesToFile(string filePath, PatchRegion[] patches)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                long fileSize = fs.Length;

                foreach (var p in patches)
                {
                    if (p.Mode == PatchMode.AppendSignature || p.Offset == -1)
                    {
                        byte[] sig = CreateSignature(p.Length > 0 ? p.Length : 128);
                        fs.Seek(0, SeekOrigin.End);
                        fs.Write(sig, 0, sig.Length);
                        continue;
                    }

                    long start = p.Offset;
                    if (start >= fileSize)
                    {
                        // 超過 EOF，跳過
                        continue;
                    }

                    int len = p.Length;
                    if (start + len > fileSize) len = (int)(fileSize - start);

                    fs.Seek(start, SeekOrigin.Begin);

                    if (p.Mode == PatchMode.XOR)
                    {
                        byte[] buffer = new byte[len];
                        int read = fs.Read(buffer, 0, len);
                        fs.Seek(start, SeekOrigin.Begin);
                        for (int i = 0; i < read; i++)
                        {
                            buffer[i] = (byte)(buffer[i] ^ p.Key);
                        }
                        fs.Write(buffer, 0, read);
                    }
                    else if (p.Mode == PatchMode.RandomOverwrite)
                    {
                        byte[] rnd = new byte[len];
                        using (var rng = RandomNumberGenerator.Create())
                        {
                            rng.GetBytes(rnd);
                        }
                        fs.Write(rnd, 0, len);
                    }
                    else // ZeroOverwrite
                    {
                        byte[] zeros = new byte[len];
                        fs.Write(zeros, 0, len);
                    }
                }

                fs.Flush(true);
            }
        }

        private static byte[] CreateSignature(int length)
        {
            string tag = "PROTECTED_BY_RFA_DESTRUCTOR";
            byte[] tagb = System.Text.Encoding.UTF8.GetBytes(tag);
            int outLen = Math.Max(length, tagb.Length);
            byte[] outb = new byte[outLen];
            Array.Copy(tagb, outb, tagb.Length);
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(outb, tagb.Length, outb.Length - tagb.Length);
            }
            return outb;
        }

        // 嘗試覆寫檔案內容後刪除，降低殘留（非保證）
        private static void TrySecureDelete(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                try
                {
                    long length = new FileInfo(path).Length;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write))
                    {
                        byte[] zeros = new byte[8192];
                        long remaining = length;
                        while (remaining > 0)
                        {
                            int write = (int)Math.Min(zeros.Length, remaining);
                            fs.Write(zeros, 0, write);
                            remaining -= write;
                        }
                        fs.Flush(true);
                    }
                }
                catch { /* 忽略覆寫異常 */ }

                File.Delete(path);
            }
            catch { /* 忽略刪除失敗 */ }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // 簡單選取過濾器：只允許 FamilyInstance
        private class FamilyInstanceSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is FamilyInstance;
            }

            public bool AllowReference(Reference reference, XYZ position)
            {
                return false;
            }
        }
    }

    // --- OverwriteFamilyLoadOptions (同前面範例) ---
    public class OverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Project;
            overwriteParameterValues = true;
            return true;
        }
    }
}
