using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using View = Autodesk.Revit.DB.View;

namespace Sinotech.CSDSEM
{
    [Transaction(TransactionMode.Manual)]
    public class AutoPipeTag : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;

            // 建立一個清單來裝所有可選的專案
            List<ProjectItem> availableProjects = new List<ProjectItem>();

            // 1. 先把【主模型】加進去
            availableProjects.Add(new ProjectItem(doc));

            // 2. 找出並把【連結模型】加進去
            FilteredElementCollector linkCollector = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance));

            foreach (RevitLinkInstance linkInst in linkCollector.Cast<RevitLinkInstance>())
            {
                Document linkedDoc = linkInst.GetLinkDocument();
                if (linkedDoc != null) // 確保連結檔已被載入
                {
                    availableProjects.Add(new ProjectItem(linkedDoc, linkInst));
                }
            }

            // 3. 呼叫 UI 介面
            using (LinkSelectionForm form = new LinkSelectionForm(availableProjects))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    // 把 List<Pipe> 換成我們新寫好的 TargetMepElement 清單
                    List<TargetMepElement> allMepElements = new List<TargetMepElement>();

                    int pipeCount = 0;
                    int ductCount = 0;
                    int cableTrayCount = 0;

                    // 建立「多類別過濾器」，效能比用迴圈慢慢判斷好很多
                    List<Type> mepTypes = new List<Type>
                    {
                        typeof(Pipe),
                        typeof(Duct),
                        typeof(CableTray)
                    };
                    ElementMulticlassFilter multiFilter = new ElementMulticlassFilter(mepTypes);

                    // 4. 根據使用者【勾選】的項目去撈管線
                    foreach (ProjectItem selectedItem in form.SelectedProjects)
                    {
                        FilteredElementCollector collector = new FilteredElementCollector(selectedItem.Doc)
                            .WherePasses(multiFilter)
                            .WhereElementIsNotElementType(); // 排除類型(Type)，只抓實體(Instance)

                        foreach (Element elem in collector)
                        {
                            // 將元素與它所屬的專案綁定起來，存入清單
                            allMepElements.Add(new TargetMepElement
                            {
                                MepElement = elem,
                                SourceProject = selectedItem
                            });

                            // 這裡示範如何【判斷是哪一種管道】並做分類統計
                            // C# 7.0 以後的 Pattern Matching 寫法 (非常適合用在這裡)
                            switch (elem)
                            {
                                case Pipe p:
                                    pipeCount++;
                                    break;
                                case Duct d:
                                    ductCount++;
                                    break;
                                case CableTray ct:
                                    cableTrayCount++;
                                    break;
                            }
                        }
                    }

                    // 假設你已經成功拿到了 allMepElements
                    if (allMepElements.Count > 0)
                    {
                        // ==========================================
                        // 步驟 A：收集主模型中的 2D 視圖並分群
                        // ==========================================

                        // 只撈取 ViewPlan (包含樓板平面、結構平面、天花板平面)
                        FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewPlan))
                            .WhereElementIsNotElementType();

                        Dictionary<string, List<View>> groupedViews = new Dictionary<string, List<View>>();

                        foreach (ViewPlan view in viewCollector.Cast<ViewPlan>())
                        {
                            // 【防呆】排除視圖樣板 (View Template)，因為樣板不能放置標籤
                            if (view.IsTemplate) continue;

                            // 取得視圖家族名稱 (例如："樓板平面圖", "結構平面")
                            string groupName = "未分類平面圖";
                            ElementId typeId = view.GetTypeId();
                            if (typeId != ElementId.InvalidElementId)
                            {
                                Element viewType = doc.GetElement(typeId);
                                if (viewType != null)
                                {
                                    groupName = viewType.Name;
                                }
                            }

                            // 將視圖加入對應的群組字典中
                            if (!groupedViews.ContainsKey(groupName))
                            {
                                groupedViews[groupName] = new List<View>();
                            }
                            groupedViews[groupName].Add(view);
                        }

                        // ==========================================
                        // 步驟 B：呼叫第二個視窗讓使用者選擇視圖
                        // ==========================================
                        using (ViewSelectionForm viewForm = new ViewSelectionForm(groupedViews))
                        {
                            if (viewForm.ShowDialog() == DialogResult.OK)
                            {
                                DateTime timeStart = DateTime.Now; // 計時開始 取得目前時間
                                int newTagCounts = 0;

                                // 拿到使用者勾選的視圖清單
                                List<View> targetViews = viewForm.SelectedViews;

                                // ==========================================
                                // 步驟 C：開啟 Transaction 開始建立 IndependentTag
                                // ==========================================
                                using (Transaction t = new Transaction(doc, "批次建立管線標籤"))
                                {
                                    t.Start();

                                    // 1. 預先取得指定的標籤 FamilySymbol
                                    FamilySymbol pipeTagSym = GetTagSymbol(doc, BuiltInCategory.OST_PipeTags, "管底_尺寸+系統");
                                    FamilySymbol ductTagSym = GetTagSymbol(doc, BuiltInCategory.OST_DuctTags, "管道標籤_寬高_高程");
                                    FamilySymbol trayTagSym = GetTagSymbol(doc, BuiltInCategory.OST_CableTrayTags, "MRT_電纜托盤編號標籤");

                                    // 確認標籤有載入，並將其 Activate (Revit API 規定使用前需確保 Active)
                                    if (pipeTagSym != null && !pipeTagSym.IsActive) pipeTagSym.Activate();
                                    if (ductTagSym != null && !ductTagSym.IsActive) ductTagSym.Activate();
                                    if (trayTagSym != null && !trayTagSym.IsActive) trayTagSym.Activate();

                                    // 如果全部都沒載入，提示使用者
                                    if (pipeTagSym == null && ductTagSym == null && trayTagSym == null)
                                    {
                                        TaskDialog.Show("警告", "找不到指定的標籤族群，請確認是否已載入專案！");
                                        t.RollBack();
                                        //return; // 或者 return Result.Failed; 取決於你的架構
                                    }

                                    // 2. 開始針對每個勾選的視圖進行處理
                                    foreach (View targetView in targetViews)
                                    {
                                        // 【關鍵優化】取得主模型在「該視圖」中『確實可見』的元件 ID 集合
                                        HashSet<ElementId> visibleMainIds = new FilteredElementCollector(doc, targetView.Id)
                                            .WhereElementIsNotElementType()
                                            .ToElementIds()
                                            .ToHashSet();

                                        // 預先過濾出「這張視圖真正需要處理」的管線：
                                        // 條件 1：它是連結模型的管線 (稍後再用 BoundingBox 判斷)
                                        // 條件 2：它是主模型的管線，且確實存在於 visibleMainIds 中
                                        List<TargetMepElement> validMepInThisView = allMepElements
                                            .Where(mep => !mep.SourceProject.IsMainModel || visibleMainIds.Contains(mep.MepElement.Id))
                                            .ToList();

                                        // 如果這張視圖裡面「一根可見的目標管線都沒有」，就直接跳過，省下大把時間！
                                        if (validMepInThisView.Count == 0) continue;
                                        // =========================================================

                                        // 注意這裡！把原本的 allMepElements 改成 validMepInThisView
                                        foreach (TargetMepElement mepItem in validMepInThisView)
                                        {
                                            Element elem = mepItem.MepElement;
                                            FamilySymbol targetSymbol = null;

                                            // 3. 判斷管線類型並對應標籤 (如果該類型的標籤沒載入就跳過)
                                            if (elem is Pipe && pipeTagSym != null) targetSymbol = pipeTagSym;
                                            else if (elem is Duct && ductTagSym != null) targetSymbol = ductTagSym;
                                            else if (elem is CableTray && trayTagSym != null) targetSymbol = trayTagSym;

                                            if (targetSymbol == null) continue;

                                            // 4. 視圖可見性判斷 (防呆機制)
                                            if (mepItem.SourceProject.IsMainModel)
                                            {
                                                // 主模型：如果不在可見清單內，直接跳過
                                                if (!visibleMainIds.Contains(elem.Id)) continue;
                                            }
                                            else
                                            {
                                                // 連結模型：API 無法直接從視圖過濾連結元件，這裡用專屬該視圖的 BoundingBox 來當作輕量級檢查
                                                BoundingBoxXYZ bboxInView = elem.get_BoundingBox(targetView);
                                                if (bboxInView == null) continue; // 回傳 null 通常代表在視圖外或被關閉顯示
                                            }

                                            // 5. 計算放置點 (管線中點) 與 Reference
                                            Reference pipeRef = null;
                                            XYZ midPoint = null;

                                            if (mepItem.SourceProject.IsMainModel)
                                            {
                                                pipeRef = new Reference(elem);
                                                midPoint = GetCurveMidPoint(elem);
                                            }
                                            else
                                            {
                                                // 連結模型專屬處理
                                                pipeRef = new Reference(elem).CreateLinkReference(mepItem.SourceProject.LinkInstance);

                                                // 【超重要】連結模型的座標必須透過 Transform 轉換回主模型的實際位置
                                                Transform linkTransform = mepItem.SourceProject.LinkInstance.GetTotalTransform();
                                                XYZ localMidPoint = GetCurveMidPoint(elem);
                                                if (localMidPoint != null)
                                                {
                                                    midPoint = linkTransform.OfPoint(localMidPoint);
                                                }
                                            }

                                            if (midPoint == null) continue; // 安全機制：萬一抓不到中心點就跳過

                                            // 6. 建立標籤
                                            IndependentTag newTag = IndependentTag.Create(
                                                doc,
                                                targetView.Id,
                                                pipeRef,
                                                true, // 不加引線 (addLeader = false)
                                                TagMode.TM_ADDBY_CATEGORY,
                                                TagOrientation.Horizontal,
                                                midPoint
                                            );

                                            // 7. 將剛建立的標籤替換成你指定的族群類型
                                            if (newTag != null)
                                            {
                                                newTag.ChangeTypeId(targetSymbol.Id);
                                                newTagCounts++;
                                            }
                                        }
                                    }

                                    t.Commit();
                                }
                                DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
                                TimeSpan totalTime = timeEnd - timeStart;
                                TaskDialog.Show("Revit", $"已產生 {newTagCounts} 個管線標籤！。\n\n" + "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒。");
                            }
                        }
                    }

                    return Result.Succeeded;
                }
            }

            return Result.Cancelled;
        }/// <summary>
         /// 取得指定的標籤族群類型 (FamilySymbol)
         /// </summary>
        private FamilySymbol GetTagSymbol(Document doc, BuiltInCategory tagCategory, string familyName)
        {
            // 利用過濾器找出對應類別的所有標籤符號
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCategory)
                .Cast<FamilySymbol>()
                .FirstOrDefault(x => x.FamilyName == familyName || x.Name == familyName);
            // 這裡同時比對 FamilyName 和 Name，避免使用者將族群名稱與類型名稱搞混
        }

        /// <summary>
        /// 取得管線元件的中心點 (0.5 參數點)
        /// </summary>
        private XYZ GetCurveMidPoint(Element elem)
        {
            // 大多數 MEP 直管都有 LocationCurve
            if (elem.Location is LocationCurve locCurve && locCurve.Curve != null)
            {
                // 傳入 0.5 並且設為 true 代表取得曲線的正中間點 (Normalized)
                return locCurve.Curve.Evaluate(0.5, true);
            }

            // 如果因為某些原因抓不到曲線，退而求其次用 BoundingBox 取幾何中心
            BoundingBoxXYZ bbox = elem.get_BoundingBox(null);
            if (bbox != null)
            {
                return (bbox.Min + bbox.Max) / 2.0;
            }

            return null;
        }
    }

    /// <summary>
    /// 裝載專案資料的類別，可支援主模型與連結模型
    /// </summary>
    public class ProjectItem
    {
        public Document Doc { get; set; }

        // 如果是主模型，這個值會是 null
        public RevitLinkInstance LinkInstance { get; set; }

        public string DisplayName { get; set; }
        public bool IsMainModel { get; set; }

        // 給「主模型」用的建構子
        public ProjectItem(Document doc)
        {
            Doc = doc;
            LinkInstance = null;
            IsMainModel = true;
            DisplayName = $"[主模型] {doc.Title}";
        }

        // 給「連結模型」用的建構子
        public ProjectItem(Document doc, RevitLinkInstance linkInstance)
        {
            Doc = doc;
            LinkInstance = linkInstance;
            IsMainModel = false;
            DisplayName = $"[連結] {doc.Title}";
        }

        public override string ToString()
        {
            return DisplayName; // 決定在 CheckedListBox 中顯示的文字
        }
    }
    /// <summary>
    /// 用來包裝撈到的 MEP 元素，以及它所屬的專案來源
    /// </summary>
    public class TargetMepElement
    {
        public Element MepElement { get; set; }
        public ProjectItem SourceProject { get; set; }

        // 輔助屬性：直接回傳它是哪種管
        public string CategoryName
        {
            get
            {
                if (MepElement is Pipe) return "水管 (Pipe)";
                if (MepElement is Duct) return "風管 (Duct)";
                if (MepElement is CableTray) return "電纜架 (CableTray)";
                return "未知類型";
            }
        }
    }
}