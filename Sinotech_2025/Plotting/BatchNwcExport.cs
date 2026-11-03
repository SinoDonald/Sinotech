using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
                {
                    TaskDialog.Show("批次匯出", "目前 Revit 未載入相容的 NWC 匯出工具。\n\n請安裝支援 Revit "
                        + uiapp.Application.VersionNumber + " 的 Navisworks NWC Export Utility，或啟用已安裝的匯出外掛，再重新啟動 Revit。\n\n不需要安裝完整的 Navisworks。安裝說明：\nhttps://www.autodesk.com/products/navisworks/3d-viewers");
                    return Result.Cancelled;
                }

                using (var settings = new SettingsForm())
                {
                    if (settings.ShowDialog(owner) != Forms.DialogResult.OK) return Result.Cancelled;
                    var files = Directory.EnumerateFiles(settings.Folder, "*", settings.Recursive
                        ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                        .Where(p => string.Equals(Path.GetExtension(p), ".rvt", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new Item(p)).ToList();
                    if (files.Count == 0)
                    {
                        TaskDialog.Show("批次匯出", "選擇的資料夾中沒有 .rvt 檔案。");
                        return Result.Cancelled;
                    }

                    bool scanCancelled;
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
                                    Validate(uiapp.Application, item.Path, settings.Overwrite);
                                    WithDocument(uiapp.Application, item.Path, doc =>
                                    {
                                        item.Views = new FilteredElementCollector(doc).OfClass(typeof(View3D))
                                            .Cast<View3D>().Where(v => !v.IsTemplate && !v.IsPerspective)
                                            .Select(v => v.Name).OrderBy(n => n, StringComparer.CurrentCulture).ToList();
                                    });
                                    if (item.Views.Count == 0) throw new InvalidOperationException("主檔沒有可匯出的正交 3D 視圖。");
                                    item.View = BatchNwcPolicy.DefaultView(item.Views, settings.PreferredView);
                                    item.Status = "待確認";
                                    item.Detail = item.View == null ? "請選擇視圖，或取消勾選此檔案。" : "";
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

                    if (!scanCancelled && files.Any(f => f.Status == "待確認"))
                    {
                        using (var selection = new SelectionForm(files))
                        {
                            if (selection.ShowDialog(owner) != Forms.DialogResult.OK) scanCancelled = true;
                        }
                    }
                    if (!scanCancelled)
                    {
                        var selected = files.Where(f => f.Selected && f.Status == "待確認").ToList();
                        using (var progress = new ProgressForm("批次匯出 NWC", selected.Count))
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
                                        Validate(uiapp.Application, item.Path, settings.Overwrite);
                                        WithDocument(uiapp.Application, item.Path, doc => Export(doc, item, settings));
                                        item.Status = "成功";
                                        item.Detail = item.Output;
                                    }
                                    catch (CloseFailedException ex)
                                    {
                                        item.Status = "失敗"; item.Detail = ex.Message;
                                        progress.CancelRequested = true;
                                        break;
                                    }
                                    catch (Exception ex) { item.Status = "失敗"; item.Detail = ex.Message; }
                                }
                            });
                        }
                    }
                    foreach (var item in files.Where(f => f.Status == "尚未處理" || f.Status == "待確認"))
                    {
                        item.Status = "略過";
                        item.Detail = scanCancelled ? "使用者取消。" : item.Selected ? "匯出已取消。" : "使用者未選取。";
                    }
                    ShowResults(owner, files, settings.Folder);
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

        private static void Validate(RevitApplication app, string path, bool overwrite)
        {
            if (BatchNwcPolicy.IsBackup(path)) throw new InvalidOperationException("Revit 自動備份檔。");
            foreach (Document doc in app.Documents)
                if (!doc.IsLinked && !string.IsNullOrEmpty(doc.PathName)
                    && string.Equals(doc.PathName, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("此檔案已在 Revit 開啟；請先關閉，以免匯出未儲存的變更。");
            if (!overwrite && File.Exists(Path.ChangeExtension(path, ".nwc")))
                throw new InvalidOperationException("同名 NWC 已存在；未啟用覆寫。");
            CheckVersions(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private static void CheckVersions(string path, HashSet<string> visited)
        {
            path = Path.GetFullPath(path);
            if (!visited.Add(path)) return;
            using (var info = BasicFileInfo.Extract(path))
                if (!info.IsSavedInCurrentVersion)
                    throw new InvalidOperationException("版本不符（" + Path.GetFileName(path) + "：Revit "
                        + info.Format + "）；只開啟與目前 Revit 同年度的檔案，不進行升級。");

            // Read only. Do not edit transmission data or unload links in the source RVT.
            // Conservatively skip a host if a saved, loaded RVT link cannot be verified.
            using (var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path))
            using (var data = TransmissionData.ReadTransmissionData(modelPath))
            {
                if (data == null) return;
                foreach (var id in data.GetAllExternalFileReferenceIds())
                {
                    var reference = data.GetLastSavedReferenceData(id);
                    if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink
                        || reference.GetLinkedFileStatus() == LinkedFileStatus.Unloaded) continue;
                    string linkedPath;
                    using (var linkedModelPath = reference.PathType == PathType.Relative ? reference.GetPath() : reference.GetAbsolutePath())
                        linkedPath = ModelPathUtils.ConvertModelPathToUserVisiblePath(linkedModelPath);
                    if (reference.PathType == PathType.Relative)
                        linkedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), linkedPath));
                    if (!File.Exists(linkedPath))
                        throw new InvalidOperationException("無法檢查已載入連結的版本或連結不存在：" + linkedPath);
                    CheckVersions(linkedPath, visited);
                }
            }
        }

        private static void WithDocument(RevitApplication app, string path, Action<Document> action)
        {
            Document document = null;
            try
            {
                using (var info = BasicFileInfo.Extract(path))
                using (var options = new OpenOptions())
                using (var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path))
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
                    }
                    catch (Exception ex)
                    {
                        throw new CloseFailedException("無法關閉批次開啟的模型，已停止後續處理。請檢查並手動關閉：\n"
                            + path + "\n" + ex.Message);
                    }
                }
            }
        }

        private static void Export(Document doc, Item item, SettingsForm settings)
        {
            var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && !v.IsPerspective && v.Name == item.View);
            if (view == null) throw new InvalidOperationException("選定的主檔 3D 視圖已不存在：" + item.View);
            string staging = Path.Combine(Path.GetDirectoryName(item.Path), ".Sinotech-NWC-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using (var options = new NavisworksExportOptions())
                {
                    options.ExportScope = NavisworksExportScope.View;
                    options.ViewId = view.Id;
                    options.ExportLinks = settings.ExportLinks;
                    options.Coordinates = settings.SharedCoordinates ? NavisworksCoordinates.Shared : NavisworksCoordinates.Internal;
                    options.FindMissingMaterials = false;
                    doc.Export(staging, Path.GetFileName(item.Output), options);
                }
                string exported = Path.Combine(staging, Path.GetFileName(item.Output));
                if (!File.Exists(exported) || new FileInfo(exported).Length == 0)
                    throw new IOException("匯出工具未產生有效的 NWC 檔案。");
                // Keep any existing deliverable intact until a complete export is available.
                if (settings.Overwrite && File.Exists(item.Output)) File.Replace(exported, item.Output, null);
                else File.Move(exported, item.Output);
            }
            finally
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine("NWC 暫存資料夾清除失敗：" + staging + " " + ex.Message); }
            }
        }

        private static void ShowResults(Forms.IWin32Window owner, List<Item> items, string folder)
        {
            string log = Path.Combine(folder, "批次匯出結果_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
            string logMessage;
            try
            {
                using (var stream = new FileStream(log, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
                {
                    writer.WriteLine("檔案,結果,主檔視圖,說明");
                    foreach (var item in items)
                        writer.WriteLine(string.Join(",", new[] { item.Path, item.Status, item.View, item.Detail }.Select(Csv)));
                }
                logMessage = "紀錄：" + log;
            }
            catch (Exception ex) { logMessage = "紀錄寫入失敗：" + ex.Message; }
            using (var form = new Forms.Form { Text = "批次匯出結果", Width = 1150, Height = 650,
                StartPosition = Forms.FormStartPosition.CenterParent, MinimizeBox = false })
            using (var grid = Grid())
            {
                grid.ReadOnly = true;
                grid.Columns.Add("File", "RVT 檔案"); grid.Columns.Add("Status", "結果");
                grid.Columns.Add("View", "主檔 3D 視圖"); grid.Columns.Add("Detail", "說明 / 輸出位置");
                foreach (var item in items) grid.Rows.Add(item.Path, item.Status, item.View, item.Detail);
                form.Controls.Add(grid);
                form.Controls.Add(new Forms.Label { Dock = Forms.DockStyle.Top, Height = 70, Padding = new Forms.Padding(8),
                    Text = "成功 " + items.Count(i => i.Status == "成功") + "；失敗 " + items.Count(i => i.Status == "失敗")
                    + "；略過 " + items.Count(i => i.Status == "略過") + "\n" + logMessage });
                form.ShowDialog(owner);
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

        private sealed class Item
        {
            internal readonly string Path;
            internal string Output { get { return System.IO.Path.ChangeExtension(Path, ".nwc"); } }
            internal List<string> Views = new List<string>();
            internal string View;
            internal string Status = "尚未處理";
            internal string Detail = "";
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

        private sealed class ProgressForm : Forms.Form
        {
            private readonly Forms.Label label = new Forms.Label { Dock = Forms.DockStyle.Top, Height = 65 };
            private readonly Forms.ProgressBar bar = new Forms.ProgressBar { Dock = Forms.DockStyle.Top };
            internal bool CancelRequested;
            internal ProgressForm(string title, int count)
            {
                Text = title; Width = 700; Height = 190; ControlBox = false;
                StartPosition = Forms.FormStartPosition.CenterParent;
                FormBorderStyle = Forms.FormBorderStyle.FixedDialog;
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
                Shown += (s, e) => { try { action(); } catch (Exception ex) { failure = ex; } finally { Close(); } };
                ShowDialog(owner);
                if (failure != null) throw failure;
            }
        }

        private sealed class SettingsForm : Forms.Form
        {
            private readonly Forms.TextBox folder = new Forms.TextBox { Dock = Forms.DockStyle.Fill, ReadOnly = true };
            private readonly Forms.TextBox preferred = new Forms.TextBox { Text = "{3D}", Dock = Forms.DockStyle.Fill };
            private readonly Forms.CheckBox recursive = new Forms.CheckBox { Text = "包含子資料夾", AutoSize = true };
            private readonly Forms.CheckBox links = new Forms.CheckBox { Text = "匯出主檔視圖內可見的 Revit 連結（可能與各主檔重複）", AutoSize = true };
            private readonly Forms.CheckBox overwrite = new Forms.CheckBox { Text = "覆寫同名 NWC（預設略過已有的 NWC）", AutoSize = true };
            private readonly Forms.CheckBox shared = new Forms.CheckBox { Text = "使用共用座標（取消則使用內部座標）", Checked = true, AutoSize = true };
            internal string Folder { get { return folder.Text; } }
            internal string PreferredView { get { return preferred.Text.Trim(); } }
            internal bool Recursive { get { return recursive.Checked; } }
            internal bool ExportLinks { get { return links.Checked; } }
            internal bool Overwrite { get { return overwrite.Checked; } }
            internal bool SharedCoordinates { get { return shared.Checked; } }
            internal SettingsForm()
            {
                Text = "批次匯出 NWC — 設定"; Width = 740; Height = 430;
                StartPosition = Forms.FormStartPosition.CenterParent;
                FormBorderStyle = Forms.FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
                var layout = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, ColumnCount = 3,
                    RowCount = 8, Padding = new Forms.Padding(12) };
                for (int i = 0; i < 7; i++) layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
                layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 130));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
                layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 95));
                var browse = new Forms.Button { Text = "選擇資料夾", AutoSize = true };
                browse.Click += (s, e) =>
                {
                    using (var picker = new Forms.FolderBrowserDialog { Description = "選擇 RVT 資料夾；NWC 會存於各 RVT 旁", ShowNewFolderButton = false })
                        if (picker.ShowDialog(this) == Forms.DialogResult.OK) folder.Text = picker.SelectedPath;
                };
                layout.Controls.Add(new Forms.Label { Text = "RVT 資料夾", AutoSize = true }, 0, 0);
                layout.Controls.Add(folder, 1, 0); layout.Controls.Add(browse, 2, 0);
                layout.Controls.Add(new Forms.Label { Text = "優先視圖完整名稱", AutoSize = true }, 0, 1);
                layout.Controls.Add(preferred, 1, 1);
                layout.Controls.Add(recursive, 1, 2); layout.Controls.Add(links, 1, 3);
                layout.Controls.Add(overwrite, 1, 4); layout.Controls.Add(shared, 1, 5);
                var help = new Forms.Label { AutoSize = true, MaximumSize = new Drawing.Size(670, 0),
                    Text = "先讀取合格模型的主檔 3D 視圖，再集中確認各檔案要輸出的視圖。\n預設 {3D}；不存在時只配對唯一的 {3D - 使用者}，其餘需手動選擇。\n只開啟同年度版本；備份、已開啟模型、版本不符或無法確認版本的已載入連結會略過。\n過程不儲存 RVT；工作共用模型以分離方式開啟。NWC 與 RVT 同名、存於同一資料夾。" };
                layout.Controls.Add(help, 0, 6); layout.SetColumnSpan(help, 3);
                var buttons = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Fill, FlowDirection = Forms.FlowDirection.RightToLeft };
                var next = new Forms.Button { Text = "讀取視圖", AutoSize = true };
                var cancel = new Forms.Button { Text = "取消", DialogResult = Forms.DialogResult.Cancel };
                next.Click += (s, e) =>
                {
                    if (!Directory.Exists(Folder) || PreferredView.Length == 0)
                    { Forms.MessageBox.Show(this, "請選擇有效的資料夾並輸入優先視圖名稱。", "批次匯出"); return; }
                    DialogResult = Forms.DialogResult.OK;
                };
                buttons.Controls.Add(next); buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 3);
                Controls.Add(layout); AcceptButton = next; CancelButton = cancel;
            }
        }

        private sealed class SelectionForm : Forms.Form
        {
            internal SelectionForm(List<Item> items)
            {
                Text = "批次匯出 — 一次確認所有主檔視圖"; Width = 1200; Height = 700;
                StartPosition = Forms.FormStartPosition.CenterParent; MinimizeBox = false;
                var grid = Grid();
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
                var panel = new Forms.FlowLayoutPanel { Dock = Forms.DockStyle.Bottom, Height = 45,
                    FlowDirection = Forms.FlowDirection.RightToLeft, Padding = new Forms.Padding(5) };
                var start = new Forms.Button { Text = "開始匯出", AutoSize = true };
                var cancel = new Forms.Button { Text = "取消", DialogResult = Forms.DialogResult.Cancel };
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
                panel.Controls.Add(start); panel.Controls.Add(cancel);
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
