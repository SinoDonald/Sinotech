using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sinotech.BatchExport
{
    internal static class BatchNwcPolicy
    {
        internal static string NormalizeFolder(string value)
        {
            return Environment.ExpandEnvironmentVariables((value ?? "").Trim().Trim('"').Trim());
        }

        internal static bool SamePath(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
            try { return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        internal static bool IsBackup(string path)
        {
            return Regex.IsMatch(path, @"\.\d{4}\.rvt$", RegexOptions.IgnoreCase);
        }

        internal static string DefaultView(IEnumerable<string> names, string preferred)
        {
            var views = names.ToList();
            string exact = views.FirstOrDefault(n => string.Equals(n, preferred, StringComparison.Ordinal));
            if (exact != null) return exact;
            // Revit may give the personal default view a name such as {3D - user}.
            // Never guess from an arbitrary 3D view or a partial custom-name match.
            if (preferred != "{3D}") return null;
            var personal = views.Where(n => n.StartsWith("{3D - ", StringComparison.Ordinal)
                && n.EndsWith("}", StringComparison.Ordinal)).ToList();
            return personal.Count == 1 ? personal[0] : null;
        }
    }

    internal sealed class BatchNwcFolderStore
    {
        private readonly string settingsPath;

        internal BatchNwcFolderStore(string settingsPath = null)
        {
            this.settingsPath = settingsPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sinotech", "BatchNwcExport", "LastFolder.txt");
        }

        internal string Load()
        {
            try { return File.Exists(settingsPath) ? BatchNwcPolicy.NormalizeFolder(File.ReadAllText(settingsPath, Encoding.UTF8)) : ""; }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine("讀取批次匯出上次路徑失敗：" + ex.Message);
                return "";
            }
        }

        internal void Save(string folder)
        {
            string fullPath = Path.GetFullPath(settingsPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, BatchNwcPolicy.NormalizeFolder(folder), new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
