using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace Sinotech_2020.Verification
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CrushReport : IExternalCommand
    {
        // Element項目
        public class ElemInfo
        {
            public Element elem = null; // Element
            public ElementId elemId = null; // Id
            public string familyName = string.Empty; // 族群名稱
            public string name = string.Empty; // 元件名稱
            public string categoryName = string.Empty; // 品類名稱
            public string builtInCategory = string.Empty; // BuiltInCategory
        }
        // TreeView的篩選元件項目
        public static List<ElemInfo> elemInfoList = new List<ElemInfo>();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            elemInfoList = FindSelectedItems(uidoc, doc); // 找到選取的元件
            CrushReportForm fvForm = new CrushReportForm(uidoc, doc);
            fvForm.TopMost = true; // 最上層顯示
            fvForm.Show();

            return Result.Succeeded;
        }
        // 找到選取的元件
        private List<ElemInfo> FindSelectedItems(UIDocument uidoc, Document doc)
        {
            List<ElemInfo> elemInfoList = new List<ElemInfo>();
            ElemInfo elemInfo = new ElemInfo();
            Element selectedElement = null;
            foreach (ElementId id in uidoc.Selection.GetElementIds())
            {
                elemInfo = new ElemInfo();
                selectedElement = doc.GetElement(id);
                // 取得Element的屬性
                Category category = selectedElement.Category;
                BuiltInCategory enumCategory = (BuiltInCategory)(int)category.Id.IntegerValue;
                elemInfo.categoryName = category.Name; // 品類名稱
                elemInfo.builtInCategory = enumCategory.ToString(); // BuiltInCategory
                Parameter para = selectedElement.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM);
                elemInfo.familyName = para.AsValueString(); // 族群名稱
                elemInfo.name = selectedElement.Name; // 元件名稱
                elemInfo.elemId = selectedElement.Id; // Id
                elemInfo.elem = selectedElement;  // Element
                elemInfoList.Add(elemInfo);
            }

            return elemInfoList;
        }
    }
}
