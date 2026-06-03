using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sinotech.CSDSEM
{
    // 自訂一個類別，用來記憶標籤原始的引線狀態與座標
    public class TagLeaderState
    {
        public bool HasLeader { get; set; }
        public LeaderEndCondition Condition { get; set; }
        public Reference HostRef { get; set; }
        public XYZ EndPosition { get; set; }
    }

    [Transaction(TransactionMode.Manual)]
    public class TagArray : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIApplication uiapp = commandData.Application;
            UIDocument uidoc = uiapp.ActiveUIDocument;
            Document doc = uidoc.Document;
            //View activeView = doc.ActiveView;

            // 選擇要輸出視圖的ViewFamilyType
            AutoNumberForm autoNumberForm = new AutoNumberForm(doc);
            autoNumberForm.ShowDialog();
            if (autoNumberForm.trueOrFalse == true)
            {
                try
                {
                    List<ViewPlan> viewPlans = AutoPipeTag.GetAutoNumberViewPlans(doc, autoNumberForm.viewFamilyTypeName); // 找到相同的ViewFamilyType與要進行編號的ViewPlan
                    ChooseMultiViewPlansForm chooseMultiViewPlansForm = new ChooseMultiViewPlansForm(doc, viewPlans);
                    chooseMultiViewPlansForm.ShowDialog();
                    List<ViewPlan> checkViewPlans = chooseMultiViewPlansForm.checkViewPlans; // 選擇要編號的ViewPlan
                    using (Transaction trans = new Transaction(doc, "標籤排序"))
                    {
                        DateTime timeStart = DateTime.Now; // 計時開始 取得目前時間
                        trans.Start();

                        foreach (ViewPlan viewPlan in checkViewPlans)
                        {
                            try
                            {
                                var tagsToMove = new FilteredElementCollector(doc, viewPlan.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();

                                //if (tagsToMove.Count == 0) return Result.Cancelled;

                                // 用來存放每個標籤原始引線狀態的字典
                                Dictionary<ElementId, TagLeaderState> leaderStates = new Dictionary<ElementId, TagLeaderState>();

                                // 1. 【狀態快照與清理】記錄所有引線狀態，然後關閉引線以取得純淨文字框
                                foreach (IndependentTag tag in tagsToMove)
                                {
                                    TagLeaderState state = new TagLeaderState();
                                    state.HasLeader = tag.HasLeader;

                                    if (tag.HasLeader)
                                    {
                                        state.Condition = tag.LeaderEndCondition;

                                        //// 2020 如果原本是自由端點(Free), 把箭頭座標備份起來, 2020直接讀取LeaderEnd屬性, 不需要Reference
                                        //if (state.Condition == LeaderEndCondition.Free)
                                        //{
                                        //    try
                                        //    {
                                        //        state.EndPosition = tag.LeaderEnd;
                                        //    }
                                        //    catch { } // 防呆保護
                                        //}

                                        // 2024 取得標籤所參考的元件 (Reference)
                                        var refs = tag.GetTaggedReferences();
                                        if (refs != null && refs.Count > 0)
                                        {
                                            state.HostRef = refs.First();

                                            // 如果原本是自由端點(Free)，把你在 AutoTag 算好的箭頭座標備份起來
                                            if (state.Condition == LeaderEndCondition.Free)
                                            {
                                                try
                                                {
                                                    state.EndPosition = tag.GetLeaderEnd(state.HostRef);
                                                }
                                                catch { } // 防呆保護
                                            }
                                        }

                                        // 記錄完畢後，關閉引線
                                        tag.HasLeader = false;
                                    }
                                    leaderStates[tag.Id] = state;
                                }

                                // 強制更新幾何，現在所有的 BoundingBox 都是乾淨的文字/符號大小
                                doc.Regenerate();

                                int scale = viewPlan.Scale;
                                double stepSize = (1.5 * scale) / 304.8;
                                double leaderThreshold = (4.0 * scale) / 304.8;
                                double padding = (1.5 * scale) / 304.8;

                                Dictionary<ElementId, Outline> allOutlines = new Dictionary<ElementId, Outline>();

                                // 2. 建立膨脹輪廓並【降維壓平 Z 軸】
                                foreach (IndependentTag tag in tagsToMove)
                                {
                                    try
                                    {
                                        BoundingBoxXYZ bb = tag.get_BoundingBox(viewPlan);
                                        if (bb != null)
                                        {
                                            double minX = bb.Min.X - padding;
                                            double minY = bb.Min.Y - padding;
                                            double maxX = bb.Max.X + padding;
                                            double maxY = bb.Max.Y + padding;

                                            // 強制設定在 Z = -1.0 到 1.0
                                            XYZ flatMin = new XYZ(minX, minY, -1.0);
                                            XYZ flatMax = new XYZ(maxX, maxY, 1.0);

                                            allOutlines[tag.Id] = new Outline(flatMin, flatMax);
                                        }
                                    }
                                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                                }

                                // 3. 螺旋避讓演算法
                                foreach (IndependentTag tag in tagsToMove)
                                {
                                    try
                                    {
                                        if (!allOutlines.ContainsKey(tag.Id)) continue;

                                        Outline myPaddedOutline = allOutlines[tag.Id];
                                        XYZ initialPos = tag.TagHeadPosition;

                                        double offsetXToMin = myPaddedOutline.MinimumPoint.X - initialPos.X;
                                        double offsetYToMin = myPaddedOutline.MinimumPoint.Y - initialPos.Y;
                                        double offsetXToMax = myPaddedOutline.MaximumPoint.X - initialPos.X;
                                        double offsetYToMax = myPaddedOutline.MaximumPoint.Y - initialPos.Y;

                                        allOutlines.Remove(tag.Id);

                                        bool isOverlapping = true;
                                        int maxIterations = 800;
                                        int iteration = 0;
                                        double angle = 0;
                                        XYZ currentPos = initialPos;
                                        Outline virtualOutline = new Outline(myPaddedOutline.MinimumPoint, myPaddedOutline.MaximumPoint);

                                        while (isOverlapping && iteration < maxIterations)
                                        {
                                            try
                                            {
                                                isOverlapping = false;

                                                foreach (Outline existing in allOutlines.Values)
                                                {
                                                    if (virtualOutline.Intersects(existing, 0))
                                                    {
                                                        isOverlapping = true;
                                                        break;
                                                    }
                                                }

                                                if (isOverlapping)
                                                {
                                                    angle += Math.PI / 4;
                                                    double radius = stepSize * ((iteration / 8) + 1);

                                                    double dx = radius * Math.Cos(angle);
                                                    double dy = radius * Math.Sin(angle);

                                                    currentPos = new XYZ(initialPos.X + dx, initialPos.Y + dy, initialPos.Z);

                                                    XYZ newMin = new XYZ(currentPos.X + offsetXToMin, currentPos.Y + offsetYToMin, -1.0);
                                                    XYZ newMax = new XYZ(currentPos.X + offsetXToMax, currentPos.Y + offsetYToMax, 1.0);
                                                    virtualOutline = new Outline(newMin, newMax);
                                                }

                                                iteration++;
                                            }
                                            catch (Exception ex) { string error = ex.ToString(); }
                                        }

                                        // 4. 更新模型實際文字位置
                                        if (!currentPos.IsAlmostEqualTo(initialPos))
                                        {
                                            tag.TagHeadPosition = currentPos;
                                        }

                                        // 5. 【狀態還原】重新套用你原本在 AutoTag.cs 設計好的引線樣式與端點座標
                                        if (leaderStates.ContainsKey(tag.Id))
                                        {
                                            TagLeaderState originalState = leaderStates[tag.Id];

                                            // 判斷是否因為演算法移動過遠，而被迫需要引線
                                            XYZ flatCurrentPos = new XYZ(currentPos.X, currentPos.Y, 0);
                                            XYZ flatInitialPos = new XYZ(initialPos.X, initialPos.Y, 0);
                                            bool forcedLeader = flatCurrentPos.DistanceTo(flatInitialPos) > leaderThreshold;

                                            if (originalState.HasLeader || forcedLeader)
                                            {
                                                tag.HasLeader = true;

                                                // 2020 不需要檢查 HostRef != null
                                                if (originalState.HasLeader && originalState.HostRef != null)
                                                {
                                                    // 恢復原本的端點條件 (Free 或 Attached)
                                                    tag.LeaderEndCondition = originalState.Condition;

                                                    // 如果原本是自由端點且有記錄座標，把箭頭精準還原到你計算的厚度位置
                                                    if (originalState.Condition == LeaderEndCondition.Free && originalState.EndPosition != null)
                                                    {
                                                        try
                                                        {
                                                            //tag.LeaderEnd = originalState.EndPosition; // 2020
                                                            tag.SetLeaderEnd(originalState.HostRef, originalState.EndPosition); // 2024
                                                        }
                                                        catch { }
                                                    }
                                                }
                                                else
                                                {
                                                    // 如果原本沒有引線，是被迫加上的，預設使用貼附
                                                    tag.LeaderEndCondition = LeaderEndCondition.Attached;
                                                }
                                            }
                                            else
                                            {
                                                tag.HasLeader = false;
                                            }
                                        }

                                        allOutlines[tag.Id] = virtualOutline;
                                    }
                                    catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                                }
                            }
                            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                        }

                        trans.Commit();
                        DateTime timeEnd = DateTime.Now; // 計時結束 取得目前時間
                        TimeSpan totalTime = timeEnd - timeStart;
                        TaskDialog.Show("Revit", "耗時：" + totalTime.Minutes + " 分 " + totalTime.Seconds + " 秒 ");
                    }
                }
                catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
            }

            return Result.Succeeded;
        }
    }
}