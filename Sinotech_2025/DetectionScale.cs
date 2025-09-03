using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Autodesk.Revit.UI;
using Autodesk.Revit.DB;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.ApplicationServices;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech_2025
{
    [Transaction(TransactionMode.Manual)]
    public class DetectionScale : IExternalCommand
    {
        public class ScaleEIdName
        {
            public ElementId eId { get; set; } // ElementId
            public string name { get; set; } // 名稱
        }
        public List<ScaleEIdName> scaleEIdNameList = new List<ScaleEIdName>(); // 儲存一般標註內所有的比例尺
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;
            
            // 儲存一般標註內所有的比例尺
            List<AnnotationSymbolType> annotationSymbolTypes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericAnnotation).WhereElementIsElementType().Cast<AnnotationSymbolType>().ToList();
            foreach(var annotationSymbolType in annotationSymbolTypes)
            {
                ScaleEIdName scaleEIdName = new ScaleEIdName();
                scaleEIdName.eId = annotationSymbolType.Id;
                scaleEIdName.name = annotationSymbolType.Name;
                scaleEIdNameList.Add(scaleEIdName);
            }
            using (Transaction trans = new Transaction(doc, "更新圖框比例尺"))
            {
                trans.Start();
                try
                {
                    DetectionVPScale(doc); // 從專案中找到全部圖紙內的ViewPlan比例尺, 並修改圖框比例尺資訊
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", ex.Message + "\n" + ex.ToString());
                }
                trans.Commit();
            }

            return Result.Succeeded;
        }
        // 從專案中找到全部圖紙內的ViewPlan比例尺, 並修改圖框比例尺資訊
        private void DetectionVPScale(Document doc)
        {
            // 無比例尺的比例
            List<int> errorScales = new List<int>();
            string errorInfo = string.Empty;

            // 找到專案中現有的所有ViewSheet
            List<ViewSheet> viewSheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList();
            foreach (ViewSheet viewSheet in viewSheets)
            {
                List<int> scales = new List<int>(); // 儲存比例尺
                ISet<ElementId> vpIds = viewSheet.GetAllPlacedViews(); // ViewSheet內的所有ViewPlan && ViewSection
                foreach(ElementId vpId in vpIds)
                {
                    Element elem = doc.GetElement(vpId);
                    if(elem is ViewPlan)
                    {
                        ViewPlan viewPlan = elem as ViewPlan;
                        Parameter vpPara = viewPlan.get_Parameter(BuiltInParameter.VIEW_SCALE); // ViewPlan的比例尺
                        scales.Add(vpPara.AsInteger()); // 儲存比例尺
                    }
                    else if(elem is ViewSection)
                    {
                        ViewSection viewScetion = elem as ViewSection;
                        Parameter vsPara = viewScetion.get_Parameter(BuiltInParameter.VIEW_SCALE); // ViewPlan的比例尺
                        scales.Add(vsPara.AsInteger()); // 儲存比例尺
                    }
                }
                var disScales = (from x in scales
                                 select x).Distinct().OrderBy(x => x).ToList(); // 移除重複並排序
                string vsSheetNumber = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                FamilyInstance vsFamilyInstance = FindTitleBlock(doc, vsSheetNumber); // 找到與圖紙內相同SheetNumber的圖框
                for (int i = 1; i <= 5; i++)
                {
                    Parameter fiPara = vsFamilyInstance.LookupParameter("Graphic Scale " + i);
                    if (i <= disScales.Count())
                    {
                        try
                        {
                            ElementId eId = (from x in scaleEIdNameList
                                             where x.name.Equals("Metric Scale " + disScales[i-1].ToString())
                                             select x.eId).FirstOrDefault();
                            fiPara.Set(eId);
                        }
                        catch (NullReferenceException)
                        {
                            errorScales.Add(disScales[i - 1]);
                        }
                    }
                    else
                    {
                        try
                        {
                            ElementId eId = (from x in scaleEIdNameList
                                             where x.name.Equals("No Scale")
                                             select x.eId).FirstOrDefault();
                            fiPara.Set(eId);
                        }
                        catch (NullReferenceException)
                        {

                        }
                    }
                }
            }
            var disErrorScales = (from x in errorScales
                                  select x).Distinct().OrderBy(x => x).ToList(); // 移除重複並排序
            if (disErrorScales.Count > 0)
            {
                errorInfo += "圖框中無 「1 : ";
                for(int i = 0; i < disErrorScales.Count; i++)
                {
                    if(i == disErrorScales.Count() - 1)
                    {
                        errorInfo += disErrorScales[i];
                    }
                    else
                    {
                        errorInfo += disErrorScales[i] + "、";
                    }
                }
                errorInfo += "」 比例尺。" + "\n";
                TaskDialog.Show("Error", errorInfo);
            }
        }
        // 找到圖紙內的圖框
        private FamilyInstance FindTitleBlock(Document doc, string vsSheetNumber)
        {
            List<FamilyInstance> allTitleBlocks = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();
            FamilyInstance vsFamilyInstance = null;
            foreach (FamilyInstance titleBlock in allTitleBlocks)
            {
                string sheetNumber = titleBlock.get_Parameter(BuiltInParameter.SHEET_NUMBER).AsString();
                if (sheetNumber.Equals(vsSheetNumber))
                {
                    vsFamilyInstance = titleBlock;
                    break;
                }
            }

            return vsFamilyInstance;
        }
    }
}
