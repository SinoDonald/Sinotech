using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class ManualPipeTag : IExternalCommand
    {
        public class OpeningInfo
        {
            public FamilyInstance opening = null; // 開口
            public Level level = null; // 樓層
            public ViewPlan viewPlan = null; // 視圖
            public string linkRvt = string.Empty; // 連結的檔案
            public int crushPipeId = 0; // 干涉的管
            public int crushElemId = 0; // 干涉的牆樑板
            public double x = 0; // 座標點X
            public double y = 0; // 座標點Y
            public double z = 0; // 座標點Z
        }
        public class PipeData
        {
            public Element elem = null; // 主體
            public List<Element> connectors = new List<Element>(); // 連結的元件
            public XYZ start = new XYZ(); // 起點
            public bool isStart = false; // 是否為起點
        }
        public class RevitLinkPipeType
        {
            public RevitLinkInstance revitLinkInstance = null; // rvt
            public string type = string.Empty; // Type
            public List<PipingSystem> pypingSystems = new List<PipingSystem>();
            public List<MechanicalSystem> mechanicalSystems = new List<MechanicalSystem>();
        }
        private List<OpeningInfo> openingInfoList = new List<OpeningInfo>(); // 依座標點排序的開口
        private static List<Level> docLevels = new List<Level>(); // Document內所有的Level
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Autodesk.Revit.ApplicationServices.Application app = uiapp.Application;
            Document doc = uidoc.Document;

            // 讀取所有Doucment的Level
            docLevels = new FilteredElementCollector(doc).OfClass(typeof(Level)).WhereElementIsNotElementType().Cast<Level>().ToList();

            // 手動標籤
            ManualTag(uidoc, doc);

            return Result.Succeeded;
        }
        // 手動標籤
        private void ManualTag(UIDocument uidoc, Document doc)
        {
            ManualTagForm autoTagForm = new ManualTagForm();
            autoTagForm.ShowDialog();
            if (autoTagForm.trueOrFalse == true)
            {
                int number = autoTagForm.number; // 起始值
                using (Transaction trans = new Transaction(doc, "AutoTag"))
                {
                    trans.Start();
                    try
                    {
                        for (int i = 0; ; i++)
                        {
                            try
                            {
                                Reference pickOne = uidoc.Selection.PickObject(Autodesk.Revit.UI.Selection.ObjectType.Element, new OpeningSelectionFilter());
                                Element elem = doc.GetElement(pickOne.ElementId);
                                FamilyInstance opening = elem as FamilyInstance;
                                Parameter para = null;
                                if (opening.Name.Equals("圓形水管牆開口") || opening.Name.Equals("圓形水管樓版開口"))
                                {
                                    para = opening.LookupParameter("圓形牆開口流水號");
                                }
                                else if (opening.Name.Equals("矩形風管牆開口") || opening.Name.Equals("電纜架牆開口") ||
                                         opening.Name.Equals("矩形風管樓版開口") || opening.Name.Equals("電纜架樓版開口"))
                                {
                                    para = opening.LookupParameter("矩形牆開口流水號");
                                }
                                para.Set(number);
                                number++;
                                doc.Regenerate();
                                uidoc.RefreshActiveView();
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException)
                            {

                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {

                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Revit", ex.Message + "\n" + ex.ToString());
                    }
                    trans.Commit();
                }
            }
        }
        // 僅能點選開口
        private class OpeningSelectionFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem is FamilyInstance && elem.Name.Contains("開口"))
                {
                    return true;
                }
                return false;
            }
            public bool AllowReference(Reference reference, XYZ position)
            {
                return true;
            }
        }
    }
}
