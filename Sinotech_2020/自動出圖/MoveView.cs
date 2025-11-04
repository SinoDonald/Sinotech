using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;
using static Sinotech_2020.CopyDrawings;

namespace Sinotech_2020
{
    [Transaction(TransactionMode.Manual)]
    public class MoveView : IExternalCommand
    {
        public static List<ViewInfo> viewInfoList = new List<ViewInfo>();
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            AllViews(doc); // 找到專案中所有視圖
            MoveViewForm moveViewForm = new MoveViewForm(uidoc, doc);
            moveViewForm.ShowDialog();

            return Result.Succeeded;
        }
        // 找到專案中所有視圖
        private void AllViews(Document doc)
        {
            viewInfoList = new List<ViewInfo>();
            // 找到所有的View
            FilteredElementCollector viewPlanCollector = new FilteredElementCollector(doc);
            List<View> views = viewPlanCollector.OfClass(typeof(View)).WhereElementIsNotElementType().Cast<View>().ToList();
            foreach (View view in views)
            {
                ViewInfo viewInfo = new ViewInfo();
                if (!view.IsTemplate && view != null) // 視圖專案中有開啟使用且不為null
                {
                    string[] viewTitle = view.Title.Split(':');
                    try
                    {
                        viewInfo.view = view;
                        viewInfo.vftName = viewTitle[0].Trim();
                        viewInfo.name = viewTitle[1].Trim();
                        if (view.GenLevel != null)
                        {
                            viewInfo.levelId = (int)view.GenLevel.Id.IntegerValue;
                        }
                        //if (!viewInfo.vftName.Contains("圖紙")) // 圖紙不包含, 避免視圖名稱相同重複複製
                        //{
                        viewInfoList.Add(viewInfo);
                        //}
                    }
                    catch (System.IndexOutOfRangeException)
                    {

                    }
                }
            }

            string viewString = string.Empty;
            // 不同的ViewFamilyType名稱
            var vftNames = (from x in viewInfoList
                            orderby x.vftName
                            select x.vftName).Distinct();
            foreach (var vftName in vftNames)
            {
                viewString += vftName + "\n";
                // 各個ViewFamilyType的樓層名稱, 依照LevelId排序
                var viewInfos = (from x in viewInfoList
                                 where x.vftName.Equals(vftName)
                                 select x).OrderBy(x => x.vftName).ThenBy(x => x.levelId);
                {
                    foreach (var viewInfo in viewInfos)
                    {
                        viewString += viewInfo.name + "\n";
                    }
                }
                viewString += "\n";
            }
        }
    }
}
