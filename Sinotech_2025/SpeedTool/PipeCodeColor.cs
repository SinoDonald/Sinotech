using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech_2025.SpeedTool
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class PipeCodeColor : IExternalEventHandler
    {
        public void Execute(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            Document doc = uidoc.Document;

            string[] rgb = PipeEditForm.color.Split(',');
            byte r = byte.Parse(rgb[0]);
            byte g = byte.Parse(rgb[1]);
            byte b = byte.Parse(rgb[2]);

            // 找到指定名稱的 PipingSystemType
            List<Element> pipingSystemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).ToElements().ToList();
            Element elem = pipingSystemTypes.FirstOrDefault(x => x.Name == PipeEditForm.code);

            using (Transaction trans = new Transaction(doc, "管線代碼與色彩修改"))
            {
                trans.Start();
                PipingSystemType pst = elem as PipingSystemType;
                // 直接修改名稱屬性
                pst.Name = PipeEditForm.newName;
                // 設定新顏色 (RGB, 每個值 0–255)
                Color newColor = new Color(r, g, b);
                pst.LineColor = newColor;
                trans.Commit();
            }
        }

        public string GetName()
        {
            return "Event handler is working now!!";
        }
    }
}
