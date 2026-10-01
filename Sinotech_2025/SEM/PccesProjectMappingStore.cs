using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace Sinotech_2025.SEM
{
    internal sealed class PccesProjectMappingStore
    {
        private readonly string filePath;

        public PccesProjectMappingStore(string filePath = null)
        {
            this.filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sinotech_2025", "PccesProjectMappings.xml");
        }

        public Dictionary<string, string> Load()
        {
            var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(filePath)) return mappings;
            var document = XDocument.Load(filePath);
            if (document.Root?.Name != "PccesProjectMappings")
                throw new InvalidDataException("PCCES 分類設定檔格式不正確。");
            foreach (var entry in document.Root.Elements("Mapping"))
            {
                string code = (string)entry.Attribute("code");
                string project = (string)entry.Attribute("project");
                if (!string.IsNullOrWhiteSpace(code) && IsValidProject(project)) mappings[code] = project;
            }
            return mappings;
        }

        public void Save(Dictionary<string, string> mappings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)));
            var root = new XElement("PccesProjectMappings");
            foreach (var mapping in mappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.Key) && IsValidProject(mapping.Value))
                    root.Add(new XElement("Mapping", new XAttribute("code", mapping.Key),
                        new XAttribute("project", mapping.Value)));
            }
            // 先完整寫入暫存檔，再取代舊檔，避免寫入中斷使既有設定遺失。
            string temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                new XDocument(root).Save(temporaryPath);
                File.Move(temporaryPath, filePath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private static bool IsValidProject(string project) => project == "A1" || project == "A2" || project == "A3";

        public static string GetDefaultProject(string code)
        {
            switch (code)
            {
                case "PS": case "COM": case "SN": case "COM/SN": case "AFC":
                    return "A1";
                case "AD": case "AP": case "EE": case "EP": case "WS": case "DS":
                case "FP": case "ECS": case "E": case "M": case "CDA":
                    return "A2";
                default:
                    return "A3";
            }
        }
    }
}
