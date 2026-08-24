using Autodesk.Revit.DB;
using System.Collections.Generic;

namespace Sinotech_2025.CSDSEM
{
    public class LinkConfigItem
    {
        public RevitLinkInstance LinkInstance { get; set; }
        public string CleanFileName { get; set; }
        public List<string> Tokens { get; set; } = new List<string>();
        public int SelectedTokenIndex { get; set; }
        public string ExtractedCode => (Tokens.Count > SelectedTokenIndex && SelectedTokenIndex >= 0)
            ? Tokens[SelectedTokenIndex]
            : string.Empty;

        public bool IsCasing { get; set; }
        public bool IsOpening { get; set; }
    }

    public class NumberingExecutionSettings
    {
        public List<string> OrderedCasingCodes { get; set; } = new List<string>();
        public List<string> OrderedOpeningCodes { get; set; } = new List<string>();
        public Dictionary<string, RevitLinkInstance> CodeToLinkMap { get; set; } = new Dictionary<string, RevitLinkInstance>();
    }
}
