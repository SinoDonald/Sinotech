using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sinotech.BatchExport
{
    internal static class BatchNwcPolicy
    {
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
}
