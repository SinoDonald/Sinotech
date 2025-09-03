using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech
{
    [Transaction(TransactionMode.Manual)]
    public class CopyDrawings : IExternalCommand
    {
        public class ViewInfo
        {
            public View view = null;
            public string vftName = string.Empty;
            public string name = string.Empty;
            public int levelId = 0;
        }
        public static List<ViewInfo> viewInfoList = new List<ViewInfo>();
        public static List<FamilySymbol> familySymbolList = new List<FamilySymbol>();
        public static FamilySymbol familySymbol = null;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Application app = uiapp.Application;
            Document doc = uidoc.Document;

            ChooseFS(doc); // 選擇圖紙類型
            if(ChooseFSForm.trueOrFalse == true)
            {
                AllViews(doc);  // 找到專案中所有視圖
                CopyViewForm copyViewForm = new CopyViewForm(doc);
                copyViewForm.ShowDialog();
            }

            return Result.Succeeded;
        }
        // 選擇圖紙類型
        private void ChooseFS(Document doc)
        {
            // 從專案中找到全部的Title Blocks
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            ICollection<Element> elems = collector.OfClass(typeof(FamilySymbol)).OfCategory(BuiltInCategory.OST_TitleBlocks).ToElements();
            familySymbolList = new List<FamilySymbol>();
            foreach (Element elem in elems)
            {
                familySymbol = elem as FamilySymbol;
                if (familySymbol != null) // 將所有FamilySymbol儲存
                {
                    familySymbolList.Add(familySymbol);
                }
            }
            // 彈跳視窗選擇FamilySymbol
            ChooseFSForm chooseFSForm = new ChooseFSForm();
            chooseFSForm.ShowDialog();
            familySymbol = chooseFSForm.familySymbol; // 回傳選擇的圖紙類型
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
                        if(view.GenLevel != null)
                        {
                            viewInfo.levelId = (int)view.GenLevel.Id.Value;
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
            foreach(var vftName in vftNames)
            {
                viewString += vftName + "\n";
                // 各個ViewFamilyType的樓層名稱, 依照LevelId排序
                var viewInfos = (from x in viewInfoList
                                 where x.vftName.Equals(vftName)
                                 select x).OrderBy(x => x.vftName).ThenBy(x => x.levelId);
                {
                    foreach(var viewInfo in viewInfos)
                    {
                        viewString += viewInfo.name + "\n";
                    }
                }
                viewString += "\n";
            }
        }
        // 新增ViewPlan
        public static void CreateViewPlan(Document doc, List<View> viewList, ViewDuplicateOption viewDuplicateOption, int start , int end)
        {
            using (Transaction trans = new Transaction(doc, "複製視圖"))
            {
                trans.Start();
                foreach (View vw in viewList)
                {
                    for(int i = start; i < end; i++)
                    {
                        // 複製並生成新的ViewPlan
                        View newView = doc.GetElement(vw.Duplicate(viewDuplicateOption)) as View;
                        // 複製檔案名稱
                        if (null != newView)
                        {
                            try
                            {
                                //newView.ViewName = vw.Name + " 複製 " + i; // 2018
                                newView.Name = vw.Name + " 複製 " + i; // 2020
                            }
                            catch (Autodesk.Revit.Exceptions.ArgumentException)
                            {

                            }
                            CreateSheetView(doc, newView); // 新增圖紙，並放置ViewPlan
                        }
                    }
                }
                trans.Commit();
            }
        }
        // 新增圖紙，並放置ViewPlan
        public static void CreateSheetView(Document doc, View view)
        {
            if (familySymbol != null)
            {
                // 新增圖紙
                ViewSheet viewSheet = ViewSheet.Create(doc, familySymbol.Id);
                if (null == viewSheet)
                {
                    throw new Exception("新增圖紙失敗.");
                }
                // 將視圖放置圖紙中心
                UV location = new UV((viewSheet.Outline.Max.U - viewSheet.Outline.Min.U) / 2, (viewSheet.Outline.Max.V - viewSheet.Outline.Min.V) / 2);
                // viewSheet.AddView(view3D, location);
                Viewport.Create(doc, viewSheet.Id, view.Id, new XYZ(location.U, location.V, 0));
            }
        }
        // ViewSheet出圖
        private void ViewSheetPrint(ViewSheet viewSheet)
        {
            if (viewSheet.CanBePrinted)
            {
                TaskDialog taskDialog = new TaskDialog("Revit");
                taskDialog.MainContent = "是否出圖？";
                TaskDialogCommonButtons buttons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;
                taskDialog.CommonButtons = buttons;
                TaskDialogResult result = taskDialog.Show();

                if (result == TaskDialogResult.Yes)
                {
                    viewSheet.Print();
                }
            }
        }
    }
}
