using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using RevitApplication = Autodesk.Revit.ApplicationServices.Application;

namespace Sinotech.BatchExport
{
    [Transaction(TransactionMode.Manual)]
    public class BatchNwcExport : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiapp = commandData.Application;
            var owner = new RevitOwner(uiapp.MainWindowHandle);
            try
            {
                using (var settings = new SettingsForm())
                {
                    while (true)
                    {
                        settings.DialogResult = Forms.DialogResult.None;
                        if (settings.ShowDialog(owner) != Forms.DialogResult.OK) return Result.Cancelled;
                        if (settings.ExportNwc && !OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
                        {
                            TaskDialog.Show("批次匯出", "目前 Revit 未載入相容的 NWC 匯出工具。\n\n請安裝支援 Revit "
                                + uiapp.Application.VersionNumber + " 的 Navisworks NWC Export Utility，或啟用已安裝的匯出外掛，再重新啟動 Revit。\n\n不需要安裝完整的 Navisworks。安裝說明：\nhttps://www.autodesk.com/products/navisworks/3d-viewers\n\n若只需要 IFC，可取消勾選 NWC 後再試一次。");
                            continue;
                        }
                        if (settings.ExportIfc && !OptionalFunctionalityUtils.IsIFCAvailable())
                        {
                            TaskDialog.Show("批次匯出", "目前 Revit 無法使用 IFC 匯出功能，請確認 Revit 安裝完整後再試一次。");
                            continue;
                        }
                        var files = Directory.EnumerateFiles(settings.Folder, "*", settings.Recursive
                            ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                            .Where(p => string.Equals(Path.GetExtension(p), ".rvt", StringComparison.OrdinalIgnoreCase))
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new Item(p)).ToList();
                        if (files.Count == 0)
                        {
                            TaskDialog.Show("批次匯出", "選擇的資料夾中沒有 .rvt 檔案。");
                            continue;
                        }

                        bool scanCancelled, closeFailed = false;
                        using (var progress = new ProgressForm("讀取主檔 3D 視圖", files.Count))
                        {
                            progress.Run(owner, () =>
                            {
                                for (int i = 0; i < files.Count; i++)
                                {
                                    if (!progress.Next(i, files[i].Path)) break;
                                    var item = files[i];
                                    try
                                    {
                                        Validate(uiapp.Application, item, settings);
                                        WithDocument(uiapp.Application, item, settings.ExportLinks, doc =>
                                        {
                                            item.Views = new FilteredElementCollector(doc).OfClass(typeof(View3D))
                                                .Cast<View3D>().Where(v => !v.IsTemplate && !v.IsPerspective)
                                                .Select(v => v.Name).OrderBy(n => n, StringComparer.CurrentCulture).ToList();
                                        });
                                        if (item.Views.Count == 0) throw new InvalidOperationException("主檔沒有可匯出的正交 3D 視圖。");
                                        item.View = BatchNwcPolicy.DefaultView(item.Views, settings.PreferredView);
                                        item.Status = "待確認";
                                        item.Detail = (item.View == null ? "請選擇視圖，或取消勾選此檔案。\n" : "") + item.NotesText;
                                    }
                                    catch (CloseFailedException ex)
                                    {
                                        item.Status = "失敗"; item.Detail = ex.Message;
                                        progress.CancelRequested = true;
                                        break;
                                    }
                                    catch (Exception ex) { item.Status = "略過"; item.Detail = ex.Message; }
                                }
                            });
                            scanCancelled = progress.CancelRequested;
                        }

                        if (!scanCancelled)
                        {
                            using (var selection = new SelectionForm(files))
                            {
                                var choice = selection.ShowDialog(owner);
                                if (choice == Forms.DialogResult.Retry) continue;
                                if (choice != Forms.DialogResult.OK) scanCancelled = true;
                            }
                        }
                        if (!scanCancelled)
                        {
                            var selected = files.Where(f => f.Selected && f.Status == "待確認").ToList();
                            using (var progress = new ProgressForm("批次匯出 " + settings.FormatsText, selected.Count))
                            {
                                progress.Run(owner, () =>
                                {
                                    for (int i = 0; i < selected.Count; i++)
                                    {
                                        if (!progress.Next(i, selected[i].Path)) break;
                                        var item = selected[i];
                                        try
                                        {
                                            // Recheck before reopening: files can change during view selection.
                                            Validate(uiapp.Application, item, settings);
                                            WithDocument(uiapp.Application, item, settings.ExportLinks, doc => Export(doc, item, settings));
                                            item.Status = item.Warnings.Count == 0 ? StatusSuccess : StatusWarning;
                                            item.Detail = string.Join("\n", item.Outputs.Concat(item.Warnings).Concat(item.Notes));
                                        }
                                        catch (CloseFailedException ex)
                                        {
                                            // An unclosed model makes further opens unsafe; stop and report the rest.
                                            item.Status = "失敗"; item.Detail = ex.Message;
                                            closeFailed = true;
                                            progress.CancelRequested = true;
                                            break;
                                        }
                                        catch (Exception ex)
                                        {
                                            // A failed file never stops the batch; keep its details for the result list.
                                            item.Status = "失敗";
                                            item.Detail = string.Join("\n", new[] { ex.Message }.Concat(item.Warnings).Concat(item.Notes));
                                        }
                                    }
                                });
                            }
                        }
                        foreach (var item in files.Where(f => f.Status == "尚未處理" || f.Status == "待確認"))
                        {
                            item.Status = "略過";
                            item.Detail = scanCancelled ? "使用者取消。" : !item.Selected ? "使用者未選取。"
                                : closeFailed ? "前一個模型無法關閉，批次已停止，此檔案尚未匯出。" : "匯出已取消。";
                        }
                        if (ShowResults(owner, files, settings.Folder) == Forms.DialogResult.Retry) continue;
                        break;
                    }
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                TaskDialog.Show("批次匯出停止", ex.Message);
                return Result.Failed;
            }
        }

        private static void Validate(RevitApplication app, Item item, SettingsForm settings)
        {
            string path = item.Path;
            if (BatchNwcPolicy.IsBackup(path)) throw new InvalidOperationException("Revit 自動備份檔。");
            // Without overwrite, skip only the formats whose output already exists.
            item.Formats = settings.Formats.Where(f => settings.Overwrite || !File.Exists(item.OutputFor(f))).ToList();
            var existing = settings.Formats.Except(item.Formats).Select(FormatName).ToList();
            if (item.Formats.Count == 0)
                throw new InvalidOperationException("同名 " + string.Join(" / ", existing) + " 已存在；未啟用覆寫。");
            if (existing.Count > 0) item.AddNote("同名 " + string.Join(" / ", existing) + " 已存在且未啟用覆寫，此格式略過。");
            // An already-open host is already compatible with this Revit session.
            // Borrow it without opening, upgrading, saving or closing it.
            if (FindOpenHost(app, path) == null) CheckFileVersion(path);
        }

        private static Document FindOpenHost(RevitApplication app, string path)
        {
            return app.Documents.Cast<Document>().FirstOrDefault(doc => doc.IsValidObject && !doc.IsLinked
                && BatchNwcPolicy.SamePath(doc.PathName, path));
        }

        private static void CheckFileVersion(string path)
        {
            using (var info = BasicFileInfo.Extract(path))
                if (!info.IsSavedInCurrentVersion)
                    throw new InvalidOperationException("版本不符（" + Path.GetFileName(path) + "：Revit "
                        + info.Format + "）；只開啟與目前 Revit 同年度的檔案，不進行升級。");
        }

        private static string ReferencePath(string sourcePath, ExternalFileReference reference)
        {
            using (var modelPath = reference.PathType == PathType.Relative ? reference.GetPath() : reference.GetAbsolutePath())
            {
                string value = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                return reference.PathType == PathType.Relative
                    ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath), value)) : value;
            }
        }

        private static ExternalFileReference SavedReference(TransmissionData data, ElementId id)
        {
            return data.IsTransmitted ? data.GetDesiredReferenceData(id) : data.GetLastSavedReferenceData(id);
        }

        // Check an included link's subtree before loading it. Failure disables that
        // top-level link in the working copy, rather than blocking the host export.
        private static void CheckLinkVersions(string path, HashSet<string> visited)
        {
            path = Path.GetFullPath(path);
            if (!visited.Add(path)) return;
            CheckFileVersion(path);
            using (var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path))
            using (var data = TransmissionData.ReadTransmissionData(modelPath))
            {
                if (data == null) return;
                foreach (var id in data.GetAllExternalFileReferenceIds())
                {
                    var reference = SavedReference(data, id);
                    if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink
                        || reference.GetLinkedFileStatus() == LinkedFileStatus.Unloaded) continue;
                    string linkedPath = ReferencePath(path, reference);
                    if (!File.Exists(linkedPath))
                        throw new InvalidOperationException("連結不存在或無法讀取：" + linkedPath);
                    CheckLinkVersions(linkedPath, visited);
                }
            }
        }

        private static string PrepareWorkingCopy(string sourcePath, string directory, bool includeLinks, Item item)
        {
            string copyPath = Path.Combine(directory, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, copyPath);
            File.SetAttributes(copyPath, File.GetAttributes(copyPath) & ~FileAttributes.ReadOnly);
            using (var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(copyPath))
            using (var data = TransmissionData.ReadTransmissionData(modelPath))
            {
                if (data == null) return copyPath;
                // Resolve paths from the original location, not from the temporary
                // directory. Only the copy's transmission data may be changed.
                var references = data.GetAllExternalFileReferenceIds()
                    .Select(id => new { Id = id, Reference = SavedReference(data, id) }).ToList();
                foreach (var entry in references)
                {
                    var reference = entry.Reference;
                    if (reference.ExternalFileReferenceType == ExternalFileReferenceType.RevitLink)
                    {
                        bool shouldLoad = includeLinks && reference.GetLinkedFileStatus() != LinkedFileStatus.Unloaded;
                        string linkedPath = null;
                        if (shouldLoad)
                        {
                            try
                            {
                                linkedPath = ReferencePath(sourcePath, reference);
                                if (!File.Exists(linkedPath)) throw new IOException("連結不存在或無法讀取：" + linkedPath);
                                CheckLinkVersions(linkedPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                            }
                            catch (Exception ex)
                            {
                                shouldLoad = false;
                                item.AddNote("未匯出連結（主檔仍繼續）：" + ex.Message);
                            }
                        }
                        if (shouldLoad)
                        {
                            using (var linkPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(linkedPath))
                                data.SetDesiredReferenceData(entry.Id, linkPath, PathType.Absolute, true);
                        }
                        else
                        {
                            using (var originalPath = reference.GetPath())
                                data.SetDesiredReferenceData(entry.Id, originalPath, reference.PathType, false);
                        }
                    }
                    else if (reference.PathType == PathType.Relative)
                    {
                        using (var absolutePath = ModelPathUtils.ConvertUserVisiblePathToModelPath(ReferencePath(sourcePath, reference)))
                            data.SetDesiredReferenceData(entry.Id, absolutePath, PathType.Absolute,
                                reference.GetLinkedFileStatus() != LinkedFileStatus.Unloaded);
                    }
                }
                data.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(modelPath, data);
            }
            return copyPath;
        }

        private static void WithDocument(RevitApplication app, Item item, bool includeLinks, Action<Document> action)
        {
            var existing = FindOpenHost(app, item.Path);
            if (existing != null)
            {
                item.AddNote("使用目前已開啟模型的內容（含未儲存變更）；不會儲存或關閉該模型。");
                if (includeLinks)
                    foreach (var link in new FilteredElementCollector(existing).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
                        if (link.GetLinkDocument() == null) item.AddNote("未匯出目前未載入的連結：" + link.Name);
                action(existing);
                return;
            }

            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "Sinotech", "BatchNwcExport", Guid.NewGuid().ToString("N"));
            Document document = null;
            bool closed = false;
            try
            {
                Directory.CreateDirectory(temporaryDirectory);
                string copyPath = PrepareWorkingCopy(item.Path, temporaryDirectory, includeLinks, item);
                item.AddNote("使用磁碟上已儲存 RVT 的暫存副本；原始檔與原始連結設定不變。");
                using (var info = BasicFileInfo.Extract(copyPath))
                using (var options = new OpenOptions())
                using (var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(copyPath))
                {
                    if (info.IsWorkshared)
                    {
                        options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
                        using (var worksets = new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets))
                            options.SetOpenWorksetsConfiguration(worksets);
                    }
                    document = app.OpenDocumentFile(modelPath, options);
                }
                action(document);
            }
            finally
            {
                if (document != null && document.IsValidObject)
                {
                    try
                    {
                        if (!document.Close(false)) throw new InvalidOperationException("Revit 未關閉文件。");
                        closed = true;
                    }
                    catch (Exception ex)
                    {
                        throw new CloseFailedException("無法關閉批次開啟的模型，已停止後續處理。請檢查並手動關閉：\n"
                            + item.Path + "\n暫存位置：" + temporaryDirectory + "\n" + ex.Message);
                    }
                }
                if (document == null || !document.IsValidObject || closed)
                {
                    try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine("RVT 暫存資料夾清除失敗：" + temporaryDirectory + " " + ex.Message); }
                }
            }
        }

        private static string FormatName(string extension) { return extension.TrimStart('.').ToUpperInvariant(); }

        private static void Export(Document doc, Item item, SettingsForm settings)
        {
            var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && v.Name == item.View);
            if (view == null) throw new InvalidOperationException("選定的主檔 3D 視圖已不存在：" + item.View);
            item.Outputs.Clear();
            var failures = new List<string>();
            foreach (string format in item.Formats)
            {
                try
                {
                    if (format == ".nwc") ExportStaged(item, format, settings.Overwrite, (folder, name) => ExportNwc(doc, view, settings, folder, name));
                    else ExportStaged(item, format, settings.Overwrite, (folder, name) => ExportIfc(doc, view, item, settings, folder, name));
                    item.Outputs.Add(item.OutputFor(format));
                }
                catch (Exception ex) { failures.Add(FormatName(format) + " 匯出失敗：" + ex.Message); }
            }
            if (failures.Count > 0)
                throw new InvalidOperationException(string.Join("\n", failures)
                    + (item.Outputs.Count == 0 ? "" : "\n已完成：" + string.Join("\n", item.Outputs)));
        }

        private static void ExportNwc(Document doc, View3D view, SettingsForm settings, string folder, string name)
        {
            using (var options = new NavisworksExportOptions())
            {
                options.ExportScope = NavisworksExportScope.View;
                options.ViewId = view.Id;
                options.ExportLinks = settings.ExportLinks;
                options.Coordinates = settings.SharedCoordinates ? NavisworksCoordinates.Shared : NavisworksCoordinates.Internal;
                options.FindMissingMaterials = false;
                doc.Export(folder, name, options);
            }
        }

        private static void ExportIfc(Document doc, View3D view, Item item, SettingsForm settings, string folder, string name)
        {
            // Federating linked models needs the IFC exporter UI; the API call exports the host model only.
            if (settings.ExportLinks && new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Any())
                item.AddNote("IFC 只含主檔模型，不含 Revit 連結。");
            using (var options = new IFCExportOptions())
            {
                options.FileVersion = settings.IfcVersion;
                options.FilterViewId = view.Id; // Only elements visible in the selected 3D view.
                options.AddOption("SitePlacement", settings.SharedCoordinates ? "0" : "3"); // SiteTransformBasis: Shared / Internal.
                // IFC export needs an open transaction, and the exporter posts per-element
                // warnings that Revit would otherwise show as a modal dialog, blocking the batch.
                // Commit the inner transaction through a preprocessor that records and clears
                // them, then roll back the group so the model is never changed.
                var collector = new FailureCollector();
                bool exported;
                using (var group = new TransactionGroup(doc, "批次匯出 IFC"))
                {
                    group.Start();
                    try
                    {
                        using (var transaction = new Transaction(doc, "批次匯出 IFC"))
                        {
                            transaction.Start();
                            var handling = transaction.GetFailureHandlingOptions();
                            handling.SetFailuresPreprocessor(collector);
                            handling.SetClearAfterRollback(true);
                            handling.SetForcedModalHandling(false);
                            transaction.SetFailureHandlingOptions(handling);
                            try { exported = doc.Export(folder, name, options); }
                            catch
                            {
                                if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack(handling);
                                throw;
                            }
                            if (transaction.GetStatus() == TransactionStatus.Started) transaction.Commit(handling);
                        }
                    }
                    finally
                    {
                        if (group.GetStatus() == TransactionStatus.Started) group.RollBack();
                    }
                }
                foreach (string warning in collector.Messages) item.AddWarning("IFC 警告：" + warning);
                if (!exported) throw new IOException("IFC 匯出工具回報失敗。");
            }
        }

        private static void ExportStaged(Item item, string format, bool overwrite, Action<string, string> export)
        {
            string output = item.OutputFor(format);
            string staging = Path.Combine(Path.GetDirectoryName(item.Path), ".Sinotech-" + FormatName(format) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                export(staging, Path.GetFileName(output));
                string exported = Path.Combine(staging, Path.GetFileName(output));
                if (!File.Exists(exported))
                {
                    // Accept a single result of the right type if the exporter renamed it (e.g. doubled extension).
                    var candidates = Directory.GetFiles(staging, "*" + format);
                    if (candidates.Length == 1) exported = candidates[0];
                }
                if (!File.Exists(exported) || new FileInfo(exported).Length == 0)
                    throw new IOException("匯出工具未產生有效的 " + FormatName(format) + " 檔案。");
                // Keep any existing deliverable intact until a complete export is available.
                if (overwrite && File.Exists(output)) File.Replace(exported, output, null);
                else File.Move(exported, output);
            }
            finally
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine(FormatName(format) + " 暫存資料夾清除失敗：" + staging + " " + ex.Message); }
            }
        }

        private const string StatusSuccess = "成功";
        private const string StatusWarning = "成功（有警告）";

        // Successful files first; files that need attention are listed last.
        private static int ResultOrder(Item item)
        {
            switch (item.Status)
            {
                case StatusSuccess: return 0;
                case StatusWarning: return 1;
                case "略過": return 2;
                default: return 3;
            }
        }

        private static string Advice(Item item)
        {
            string detail = item.Detail ?? "";
            switch (item.Status)
            {
                case StatusSuccess:
                    return "";
                case StatusWarning:
                    return "檔案已產生，但列出的元素可能未正確匯出至 IFC。請在 Revit 用「管理 > 查詢 > 依 ID 選取」找到這些元素，"
                        + "檢查其族群幾何是否異常；修正或在匯出視圖中隱藏後，再勾選覆寫重新匯出此檔。若不影響使用可忽略。";
                case "略過":
                    if (detail.Contains("版本不符")) return "請用該檔案對應年度的 Revit 開啟匯出，或先另存升級為目前版本後再匯出。";
                    if (detail.Contains("已存在")) return "如需更新，請按「上一步」勾選「覆寫同名 NWC / IFC」後重新匯出。";
                    if (detail.Contains("備份檔")) return "Revit 自動備份檔，不需處理。";
                    if (detail.Contains("無法關閉")) return "請先手動關閉上一個未關閉的模型，再按「上一步」重新匯出此檔。";
                    if (detail.Contains("沒有可匯出")) return "請在主檔建立一個正交 3D 視圖後再匯出。";
                    if (detail.Contains("使用者")) return "";
                    return "請依說明處理後，按「上一步」重新匯出此檔。";
                default:
                    if (detail.Contains("無法關閉")) return "請在 Revit 中檢查並手動關閉該模型，再按「上一步」重新匯出未完成的檔案。";
                    if (detail.Contains("視圖已不存在")) return "請按「上一步」重新讀取視圖並選擇此檔的 3D 視圖。";
                    if (detail.Contains("NWC 匯出")) return "請確認 Navisworks NWC Export Utility 可正常使用，或改用 Revit「匯出 > NWC」手動匯出此檔以查看詳細訊息。";
                    if (detail.Contains("IFC 匯出")) return "請在 Revit 開啟此檔，用「檔案 > 匯出 > IFC」手動匯出同一視圖，查看詳細錯誤後修正模型再重新匯出。";
                    if (detail.Contains("連結")) return "請檢查連結路徑與版本，或取消「匯出連結」後重新匯出。";
                    return "請依說明處理後，按「上一步」只勾選此檔重新匯出；若持續失敗，請將 CSV 紀錄提供給維護人員。";
            }
        }

        private static Forms.DialogResult ShowResults(Forms.IWin32Window owner, List<Item> items, string folder)
        {
            items = items.OrderBy(ResultOrder).ToList();
            string log = Path.Combine(folder, "批次匯出結果_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
            string logMessage;
            try
            {
                using (var stream = new FileStream(log, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine("檔案,結果,主檔視圖,說明,處置建議");
                    foreach (var item in items)
                        writer.WriteLine(string.Join(",", new[] { item.Path, item.Status, item.View, item.Detail, Advice(item) }.Select(Csv)));
                }
                logMessage = "紀錄：" + log;
            }
            catch (Exception ex) { logMessage = "紀錄寫入失敗：" + ex.Message; }
            using (var form = new Forms.Form { Text = "批次匯出結果", Width = 1250, Height = 760,
                StartPosition = Forms.FormStartPosition.CenterParent, MinimizeBox = false })
            using (var grid = Grid())
            {
                ApplyIcon(form);
                grid.ReadOnly = true;
                grid.DefaultCellStyle.WrapMode = Forms.DataGridViewTriState.True;
                grid.AutoSizeRowsMode = Forms.DataGridViewAutoSizeRowsMode.AllCells;
                grid.Columns.Add("File", "RVT 檔案"); grid.Columns.Add("Status", "結果");
                grid.Columns.Add("View", "主檔 3D 視圖"); grid.Columns.Add("Detail", "說明 / 輸出位置");
                grid.Columns.Add("Advice", "處置建議");
                grid.Columns["Status"].FillWeight = 40; grid.Columns["View"].FillWeight = 50;
                grid.Columns["Detail"].FillWeight = 160; grid.Columns["Advice"].FillWeight = 120;
                foreach (var item in items)
                {
                    int index = grid.Rows.Add(item.Path, item.Status, item.View, item.Detail, Advice(item));
                    if (item.Status == "失敗") grid.Rows[index].DefaultCellStyle.BackColor = Drawing.Color.MistyRose;
                    else if (item.Status == StatusWarning) grid.Rows[index].DefaultCellStyle.BackColor = Drawing.Color.LemonChiffon;
                }
                form.Controls.Add(grid);
                // Gather everything that needs the user's attention at the bottom, with what to do about it.
                var problems = items.Where(i => Advice(i).Length > 0).ToList();
                if (problems.Count > 0)
                {
                    var summary = new Forms.TextBox { Dock = Forms.DockStyle.Bottom, Height = 200, Multiline = true, ReadOnly = true,
                        ScrollBars = Forms.ScrollBars.Vertical, BackColor = Drawing.SystemColors.Window,
                        Text = "【需要處理的項目】共 " + problems.Count + " 個\r\n\r\n" + string.Join("\r\n\r\n", problems.Select((p, n) =>
                            (n + 1) + ". [" + p.Status + "] " + p.Path + "\r\n   說明：" + (p.Detail ?? "").Replace("\n", "\r\n   ")
                            + "\r\n   處置：" + Advice(p))) };
                    form.Controls.Add(summary);
                }
                var buttons = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 45,
                    FlowDirection = Forms.FlowDirection.RightToLeft, Padding = new Forms.Padding(5) };
                var close = new Forms.Button { Text = "關閉", DialogResult = Forms.DialogResult.Cancel, AutoSize = true };
                var back = new Forms.Button { Text = "上一步", DialogResult = Forms.DialogResult.Retry, AutoSize = true };
                buttons.Controls.Add(close); buttons.Controls.Add(back);
                form.Controls.Add(buttons);
                form.AcceptButton = close; form.CancelButton = close;
                form.Controls.Add(new Forms.Label { Dock = Forms.DockStyle.Top, Height = 70, Padding = new Forms.Padding(8),
                    Text = "成功 " + items.Count(i => i.Status == StatusSuccess) + "；成功（有警告） " + items.Count(i => i.Status == StatusWarning)
                    + "；失敗 " + items.Count(i => i.Status == "失敗")
                    + "；略過 " + items.Count(i => i.Status == "略過") + "\n" + logMessage });
                return form.ShowDialog(owner);
            }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@\t\r".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static Forms.DataGridView Grid()
        {
            return new Forms.DataGridView { Dock = Forms.DockStyle.Fill, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = Forms.DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false, BackgroundColor = Drawing.SystemColors.Window };
        }

        private static void ApplyIcon(Forms.Form form)
        {
            using (var stream = typeof(BatchNwcExport).Assembly.GetManifestResourceStream("Sinotech.BatchExport.Sinotech.ico"))
            {
                if (stream == null) throw new InvalidOperationException("找不到批次匯出視窗圖示 Sinotech.ico。");
                using (var icon = new Drawing.Icon(stream)) form.Icon = (Drawing.Icon)icon.Clone();
                form.ShowIcon = true;
            }
        }

        // Both .NET Framework and .NET 8 use the same Windows Explorer folder
        // picker. The Framework FolderBrowserDialog otherwise uses the old tree UI.
        private static class FolderPicker
        {
            internal static string Show(Forms.IWin32Window owner, string initialFolder)
            {
                object instance = null;
                IShellItem initial = null;
                IShellItem result = null;
                IntPtr name = IntPtr.Zero;
                try
                {
                    instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")));
                    var dialog = (IFileOpenDialog)instance;
                    dialog.GetOptions(out uint options);
                    dialog.SetOptions(options | 0x20u | 0x40u | 0x800u | 0x8u); // Folders, filesystem, existing path, no CWD change.
                    dialog.SetTitle("選擇 RVT 資料夾");
                    dialog.SetOkButtonLabel("選擇資料夾");
                    if (Directory.Exists(initialFolder))
                    {
                        var iid = typeof(IShellItem).GUID;
                        int createResult = SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref iid, out initial);
                        if (createResult >= 0) dialog.SetFolder(initial);
                    }
                    int showResult = dialog.Show(owner.Handle);
                    if (showResult == unchecked((int)0x800704C7)) return null;
                    Marshal.ThrowExceptionForHR(showResult);
                    dialog.GetResult(out result);
                    result.GetDisplayName(0x80058000, out name); // SIGDN_FILESYSPATH
                    return Marshal.PtrToStringUni(name);
                }
                finally
                {
                    if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name);
                    if (result != null) Marshal.FinalReleaseComObject(result);
                    if (initial != null) Marshal.FinalReleaseComObject(initial);
                    if (instance != null) Marshal.FinalReleaseComObject(instance);
                }
            }

            [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
            private static extern int SHCreateItemFromParsingName(string path, IntPtr bindingContext, ref Guid iid,
                [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

            [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IFileOpenDialog
            {
                [PreserveSig] int Show(IntPtr owner);
                void SetFileTypes(uint count, IntPtr filters);
                void SetFileTypeIndex(uint index);
                void GetFileTypeIndex(out uint index);
                void Advise(IntPtr events, out uint cookie);
                void Unadvise(uint cookie);
                void SetOptions(uint options);
                void GetOptions(out uint options);
                void SetDefaultFolder(IShellItem folder);
                void SetFolder(IShellItem folder);
                void GetFolder(out IShellItem folder);
                void GetCurrentSelection(out IShellItem item);
                void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
                void GetFileName(out IntPtr name);
                void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
                void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
                void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
                void GetResult(out IShellItem item);
            }

            [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IShellItem
            {
                void BindToHandler(IntPtr bindingContext, ref Guid handler, ref Guid iid, out IntPtr value);
                void GetParent(out IShellItem parent);
                void GetDisplayName(uint type, out IntPtr name);
                void GetAttributes(uint mask, out uint attributes);
                void Compare(IShellItem other, uint hint, out int order);
            }
        }

        private sealed class Item
        {
            internal readonly string Path;
            internal string OutputFor(string extension) { return System.IO.Path.ChangeExtension(Path, extension); }
            internal List<string> Formats = new List<string>();
            internal readonly List<string> Outputs = new List<string>();
            internal List<string> Views = new List<string>();
            internal string View;
            internal string Status = "尚未處理";
            internal string Detail = "";
            internal readonly List<string> Notes = new List<string>();
            internal string NotesText { get { return string.Join("\n", Notes); } }
            internal void AddNote(string note) { if (!Notes.Contains(note)) Notes.Add(note); }
            internal readonly List<string> Warnings = new List<string>();
            internal void AddWarning(string warning) { if (!Warnings.Contains(warning)) Warnings.Add(warning); }
            internal bool Selected;
            internal Item(string path) { Path = path; }
        }

        private sealed class RevitOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            internal RevitOwner(IntPtr handle) { Handle = handle; }
        }

        private sealed class CloseFailedException : Exception
        {
            internal CloseFailedException(string message) : base(message) { }
        }

        // Records exporter failures and clears warnings so no modal Revit dialog interrupts the batch.
        private sealed class FailureCollector : IFailuresPreprocessor
        {
            private const int MaxListedElements = 10;
            internal readonly List<string> Messages = new List<string>();

            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                bool hasError = false;
                var document = accessor.GetDocument();
                foreach (var group in accessor.GetFailureMessages().GroupBy(f => f.GetDescriptionText()))
                {
                    var ids = group.SelectMany(f => f.GetFailingElementIds()).Distinct().ToList();
                    var names = ids.Take(MaxListedElements).Select(id =>
                    {
                        var element = document.GetElement(id);
                        string label = element == null ? "" : (element.Category != null ? element.Category.Name + " : " : "") + element.Name + " : ";
                        return label + "ID " + id;
                    }).ToList();
                    if (ids.Count > MaxListedElements) names.Add("…等共 " + ids.Count + " 個元素");
                    Messages.Add(group.Key + (names.Count == 0 ? "" : "\n  " + string.Join("\n  ", names)));
                    foreach (var failure in group)
                    {
                        if (failure.GetSeverity() == FailureSeverity.Warning) accessor.DeleteWarning(failure);
                        else hasError = true;
                    }
                }
                return hasError ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
            }
        }

        private sealed class ProgressForm : Forms.Form
        {
            private readonly Forms.Label label = new Forms.Label { Dock = Forms.DockStyle.Top, Height = 65 };
            private readonly Forms.ProgressBar bar = new Forms.ProgressBar { Dock = Forms.DockStyle.Top };
            internal bool CancelRequested;
            private bool completed;
            internal ProgressForm(string title, int count)
            {
                ApplyIcon(this);
                Text = title; Width = 700; Height = 190; MaximizeBox = false; MinimizeBox = false;
                StartPosition = Forms.FormStartPosition.CenterParent;
                FormBorderStyle = Forms.FormBorderStyle.FixedSingle;
                FormClosing += (s, e) => { if (!completed) { CancelRequested = true; e.Cancel = true; } };
                bar.Maximum = Math.Max(1, count);
                var cancel = new Forms.Button { Text = "取消後續檔案", Dock = Forms.DockStyle.Bottom, Height = 32 };
                cancel.Click += (s, e) => { CancelRequested = true; cancel.Enabled = false; label.Text = "正在完成目前檔案並關閉模型…"; };
                Controls.Add(cancel); Controls.Add(bar); Controls.Add(label);
            }
            internal bool Next(int index, string path)
            {
                label.Text = (index + 1) + " / " + bar.Maximum + "\n" + path;
                bar.Value = index;
                Forms.Application.DoEvents();
                return !CancelRequested;
            }
            internal void Run(Forms.IWin32Window owner, Action action)
            {
                Exception failure = null;
                Shown += (s, e) => { try { action(); } catch (Exception ex) { failure = ex; } finally { completed = true; Close(); } };
                ShowDialog(owner);
                if (failure != null) throw failure;
            }
        }

        private sealed class SettingsForm : Forms.Form
        {
            private readonly Forms.TextBox folder = new Forms.TextBox { Dock = Forms.DockStyle.Fill };
            private readonly BatchNwcFolderStore folderStore;
            private readonly Forms.TextBox preferred = new Forms.TextBox { Text = "{3D}", Dock = Forms.DockStyle.Fill };
            private readonly Forms.CheckBox recursive = new Forms.CheckBox { Text = "包含子資料夾", AutoSize = true };
            private readonly Forms.CheckBox links = new Forms.CheckBox { Text = "匯出主檔視圖內可見的 Revit 連結（可能與各主檔重複）", AutoSize = true };
            private readonly Forms.CheckBox overwrite = new Forms.CheckBox { Text = "覆寫同名 NWC / IFC（預設略過已存在的檔案）", AutoSize = true };
            private readonly Forms.CheckBox nwc = new Forms.CheckBox { Text = "NWC", Checked = true, AutoSize = true };
            private readonly Forms.CheckBox ifc = new Forms.CheckBox { Text = "IFC", AutoSize = true };
            private readonly Forms.ComboBox ifcVersion = new Forms.ComboBox { DropDownStyle = Forms.ComboBoxStyle.DropDownList, Width = 260 };
            private static readonly KeyValuePair<string, IFCVersion>[] IfcVersions =
            {
                new KeyValuePair<string, IFCVersion>("IFC2x3 Coordination View 2.0", IFCVersion.IFC2x3CV2),
                new KeyValuePair<string, IFCVersion>("IFC4 Reference View", IFCVersion.IFC4RV),
                new KeyValuePair<string, IFCVersion>("IFC4 Design Transfer View", IFCVersion.IFC4DTV),
            };
            private readonly Forms.CheckBox shared = new Forms.CheckBox { Text = "使用共用座標（取消則使用內部座標）", Checked = true, AutoSize = true };
            internal string Folder { get { return BatchNwcPolicy.NormalizeFolder(folder.Text); } }
            internal string PreferredView { get { return preferred.Text.Trim(); } }
            internal bool Recursive { get { return recursive.Checked; } }
            internal bool ExportLinks { get { return links.Checked; } }
            internal bool Overwrite { get { return overwrite.Checked; } }
            internal bool SharedCoordinates { get { return shared.Checked; } }
            internal bool ExportNwc { get { return nwc.Checked; } }
            internal bool ExportIfc { get { return ifc.Checked; } }
            internal IFCVersion IfcVersion { get { return IfcVersions[Math.Max(0, ifcVersion.SelectedIndex)].Value; } }
            internal List<string> Formats
            {
                get
                {
                    var formats = new List<string>();
                    if (ExportNwc) formats.Add(".nwc");
                    if (ExportIfc) formats.Add(".ifc");
                    return formats;
                }
            }
            internal string FormatsText { get { return string.Join(" / ", Formats.Select(FormatName)); } }
            internal SettingsForm(BatchNwcFolderStore folderStore = null)
            {
                ApplyIcon(this);
                this.folderStore = folderStore ?? new BatchNwcFolderStore();
                folder.Text = this.folderStore.Load();
                Text = "批次匯出 NWC / IFC — 設定"; Width = 740; Height = 490;
                StartPosition = Forms.FormStartPosition.CenterParent;
                FormBorderStyle = Forms.FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
                var layout = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, ColumnCount = 3,
                    RowCount = 9, Padding = new Forms.Padding(12) };
                for (int i = 0; i < 8; i++) layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
                layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 130));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 95));
                var browse = new Forms.Button { Text = "選擇資料夾", AutoSize = true };
                browse.Click += (s, e) =>
                {
                    try
                    {
                        string selected = FolderPicker.Show(this, Folder);
                        if (selected != null)
                        {
                            folder.Text = selected;
                            RememberFolder();
                        }
                    }
                    catch (Exception ex) { Forms.MessageBox.Show(this, "無法開啟資料夾選擇視窗，可直接在欄位貼上路徑：\n" + ex.Message, "批次匯出"); }
                };
                layout.Controls.Add(new Forms.Label { Text = "RVT 資料夾\n（可直接貼上路徑）", AutoSize = true }, 0, 0);
                layout.Controls.Add(folder, 1, 0); layout.Controls.Add(browse, 2, 0);
                layout.Controls.Add(new Forms.Label { Text = "優先視圖名稱", AutoSize = true }, 0, 1);
                layout.Controls.Add(preferred, 1, 1);
                ifcVersion.Items.AddRange(IfcVersions.Select(v => (object)v.Key).ToArray());
                ifcVersion.SelectedIndex = 0;
                ifcVersion.Enabled = false;
                ifc.CheckedChanged += (s, e) => ifcVersion.Enabled = ifc.Checked;
                var formatPanel = new Forms.FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Forms.Padding(0) };
                formatPanel.Controls.Add(nwc); formatPanel.Controls.Add(ifc);
                formatPanel.Controls.Add(new Forms.Label { Text = "IFC 版本", AutoSize = true, Padding = new Forms.Padding(12, 6, 0, 0) });
                formatPanel.Controls.Add(ifcVersion);
                layout.Controls.Add(new Forms.Label { Text = "匯出格式", AutoSize = true }, 0, 2);
                layout.Controls.Add(formatPanel, 1, 2); layout.SetColumnSpan(formatPanel, 2);
                layout.Controls.Add(recursive, 1, 3); layout.Controls.Add(links, 1, 4);
                layout.Controls.Add(overwrite, 1, 5); layout.Controls.Add(shared, 1, 6);
                var help = new Forms.Label { AutoSize = true, MaximumSize = new Drawing.Size(670, 0),
                    Text = "可直接貼上資料夾路徑，下次會帶入上次確認的路徑。\n優先視圖名稱依「包含文字」配對（不分大小寫），例如 Navi 可找到 Naviswork。\n完全同名優先；其餘多個符合時請手動選擇。可按「上一步」返回修改設定。\n已開啟主檔使用目前內容（含未儲存變更）；其他檔案使用已儲存 RVT 的暫存副本。\n有問題的連結會略過並提醒；主檔版本不符或為備份則略過。NWC / IFC 存於 RVT 旁，原始 RVT 不儲存。\nIFC 依選定 3D 視圖的可見元素匯出，只含主檔，不含 Revit 連結。" };
                layout.Controls.Add(help, 0, 7); layout.SetColumnSpan(help, 3);
                var buttons = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.RightToLeft };
                var next = new Forms.Button { Text = "讀取視圖", AutoSize = true };
                var cancel = new Forms.Button { Text = "取消", DialogResult = Forms.DialogResult.Cancel };
                next.Click += (s, e) =>
                {
                    if (!Directory.Exists(Folder) || PreferredView.Length == 0)
                    { Forms.MessageBox.Show(this, "請選擇有效的資料夾並輸入優先視圖名稱（可輸入部分文字）。", "批次匯出"); return; }
                    if (Formats.Count == 0)
                    { Forms.MessageBox.Show(this, "請至少勾選一種匯出格式（NWC 或 IFC）。", "批次匯出"); return; }
                    folder.Text = Path.GetFullPath(Folder);
                    RememberFolder();
                    DialogResult = Forms.DialogResult.OK;
                };
                buttons.Controls.Add(next); buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 3);
                Controls.Add(layout); AcceptButton = next; CancelButton = cancel;
            }

            private void RememberFolder()
            {
                try { folderStore.Save(Folder); }
                catch (Exception ex) { Forms.MessageBox.Show(this, "本次仍可匯出，但無法記住資料夾路徑：\n" + ex.Message, "批次匯出"); }
            }
        }

        private sealed class SelectionForm : Forms.Form
        {
            internal SelectionForm(List<Item> items)
            {
                ApplyIcon(this);
                Text = "批次匯出 — 一次確認所有主檔視圖"; Width = 1200; Height = 700;
                StartPosition = Forms.FormStartPosition.CenterParent; MinimizeBox = false;
                var grid = Grid();
                grid.EditMode = Forms.DataGridViewEditMode.EditOnEnter;
                grid.Columns.Add(new Forms.DataGridViewCheckBoxColumn { Name = "Selected", HeaderText = "匯出", FillWeight = 25 });
                grid.Columns.Add("File", "RVT 檔案");
                grid.Columns.Add(new Forms.DataGridViewComboBoxColumn { Name = "View", HeaderText = "主檔 3D 視圖", DisplayStyle = Forms.DataGridViewComboBoxDisplayStyle.DropDownButton });
                grid.Columns.Add("Detail", "檢查結果");
                grid.Columns["File"].ReadOnly = true; grid.Columns["Detail"].ReadOnly = true;
                foreach (var item in items)
                {
                    int index = grid.Rows.Add(item.Status == "待確認", item.Path, null, item.Status + " " + item.Detail);
                    var row = grid.Rows[index]; row.Tag = item;
                    var cell = (Forms.DataGridViewComboBoxCell)row.Cells["View"];
                    cell.Items.AddRange(item.Views.Cast<object>().ToArray());
                    cell.Value = item.View;
                    if (item.Status != "待確認")
                    { row.ReadOnly = true; row.DefaultCellStyle.BackColor = Drawing.Color.Gainsboro; }
                    else if (item.View == null) row.DefaultCellStyle.BackColor = Drawing.Color.LemonChiffon;
                }
                grid.CurrentCellDirtyStateChanged += (s, e) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(Forms.DataGridViewDataErrorContexts.Commit); };
                grid.CellMouseDown += (s, e) =>
                {
                    if (e.Button != Forms.MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex < 0
                        || grid.Columns[e.ColumnIndex].Name != "View" || grid.Rows[e.RowIndex].ReadOnly) return;
                    var target = grid.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    // Wait for the grid's own mouse handling, which otherwise
                    // consumes the first click or toggles the new editor closed.
                    grid.BeginInvoke(new Action(() =>
                    {
                        if (grid.IsDisposed || target.ReadOnly) return;
                        grid.CurrentCell = target;
                        grid.BeginEdit(true);
                        if (grid.EditingControl is Forms.ComboBox combo)
                            combo.DroppedDown = true;
                    }));
                };
                var panel = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 45,
                    FlowDirection = Forms.FlowDirection.RightToLeft, Padding = new Forms.Padding(5) };
                var start = new Forms.Button { Text = "開始匯出", AutoSize = true, Enabled = items.Any(i => i.Status == "待確認") };
                var cancel = new Forms.Button { Text = "取消", DialogResult = Forms.DialogResult.Cancel };
                var back = new Forms.Button { Text = "上一步", DialogResult = Forms.DialogResult.Retry };
                start.Click += (s, e) =>
                {
                    grid.EndEdit();
                    foreach (Forms.DataGridViewRow row in grid.Rows)
                    {
                        var item = (Item)row.Tag;
                        item.Selected = item.Status == "待確認" && Convert.ToBoolean(row.Cells["Selected"].Value);
                        item.View = row.Cells["View"].Value as string;
                        if (item.Selected && !item.Views.Contains(item.View))
                        {
                            grid.CurrentCell = row.Cells["View"];
                            Forms.MessageBox.Show(this, "請替勾選的檔案選擇主檔 3D 視圖，或取消勾選。\n" + item.Path, "批次匯出");
                            return;
                        }
                    }
                    if (!items.Any(i => i.Selected)) { Forms.MessageBox.Show(this, "請至少勾選一個檔案。", "批次匯出"); return; }
                    DialogResult = Forms.DialogResult.OK;
                };
                panel.Controls.Add(start); panel.Controls.Add(cancel); panel.Controls.Add(back);
                Controls.Add(grid); Controls.Add(panel);
                Controls.Add(new Forms.Label { Dock = Forms.DockStyle.Top, Height = 55, Padding = new Forms.Padding(8),
                    Text = "只列出各 RVT 主檔的非樣板、非透視 3D 視圖。黃色列需選擇視圖；灰色列已略過。\n可取消勾選個別檔案。按「開始匯出」後，依本次選擇處理全部檔案，不再逐檔詢問視圖。" });
                AcceptButton = start; CancelButton = cancel;
            }
        }
    }

    public class BatchNwcExportAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories) { return true; }
    }
}
