using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

using System.Windows.Forms;

namespace SpeedTools
{
    public partial class AutoJoinForm : System.Windows.Forms.Form
    {
        private UIDocument uidoc;
        private Document doc;
        private Autodesk.Revit.DB.View activeView;
        List<string> checkedNodesList = new List<string>(); // 儲存選取的節點

        public AutoJoinForm(UIDocument autoJoinUidoc, Document autoJoinDoc, Autodesk.Revit.DB.View autoJoinActiveView)
        {
            uidoc = autoJoinUidoc;
            doc = autoJoinDoc;
            activeView = autoJoinActiveView;

            InitializeComponent();
            treeView1.ExpandAll(); // 節點全部展開

            // 畫面置中
            CenterToScreen();
        }
        // 確定
        private void SureBtn_Click(object sender, EventArgs e)
        {
            // host, 主體接合元件
            FilteredElementCollector host1Coll = new FilteredElementCollector(doc, activeView.Id);
            string radioChecked = string.Empty; // host選擇的品類
            foreach (RadioButton radioBtn in this.groupBox1.Controls)
            {
                if(radioBtn != null && radioBtn.Checked == true) // 是RadioButton, 且勾選
                {
                    radioChecked = radioBtn.Text;
                    break;
                }
            }
            ICollection<Element> hostElems = FilterCondition(host1Coll, radioChecked); // 收集所有主體接合元件的Element
            // 找到TreeView中有勾選的節點名稱
            List<string> tNsChecked = new List<string>();
            foreach (TreeNode all in treeView1.Nodes) // 全選
            {
                foreach(TreeNode node in all.Nodes) // 柱樑牆板
                {
                    if (node.Checked == true)
                    {
                        tNsChecked.Add(node.Text);
                    }
                }
            }
            // 選擇自動接合或取消接合
            if (hostElems != null && hostElems.Count != 0)
            {
                string joinOrUnjoin = string.Empty;
                if (joinCancel.Checked == true)
                {
                    joinOrUnjoin = "取消接合";
                    AutoJoinOrUnjoin(hostElems, tNsChecked, joinOrUnjoin);
                }
                else
                {
                    joinOrUnjoin = "自動接合";
                    AutoJoinOrUnjoin(hostElems, tNsChecked, joinOrUnjoin);
                }
            }

            Close();
        }
        // 取消
        private void CancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
        // 過濾條件
        private ICollection<Element> FilterCondition(FilteredElementCollector hostColl, string chooseCategory)
        {
            ICollection<Element> hostElems = new List<Element>();

            if (chooseCategory.Equals("柱")) // 柱+結構柱
            {
                ElementCategoryFilter columns = new ElementCategoryFilter(BuiltInCategory.OST_Columns);
                ElementCategoryFilter sColumns = new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns);
                LogicalOrFilter columnsFilter = new LogicalOrFilter(sColumns, columns);
                hostElems = hostColl.WherePasses(columnsFilter).WhereElementIsNotElementType().ToElements();
            }
            else if (chooseCategory.Equals("樑")) // 樑
            {
                hostElems = hostColl.OfCategory(BuiltInCategory.OST_StructuralFraming).WhereElementIsNotElementType().ToElements();
            }
            else if (chooseCategory.Equals("牆")) // 牆
            {
                hostElems = hostColl.OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().ToElements();
            }
            else if (chooseCategory.Equals("板")) // 板
            {
                hostElems = hostColl.OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().ToElements();
            }

            return hostElems;
        }
        // 執行透過BoundingBox找到的交錯元件自動接合, 或者取消接合
        private void AutoJoinOrUnjoin(ICollection<Element> element, List<string> tNsChecked, string joinOrUnjoin)
        {
            try
            {
                using (Transaction trans = new Transaction(doc))
                {
                    // 關閉警示視窗
                    FailureHandlingOptions options = trans.GetFailureHandlingOptions();
                    MyPreProcessor preproccessor = new MyPreProcessor();
                    options.SetClearAfterRollback(true);
                    options.SetFailuresPreprocessor(preproccessor);
                    trans.SetFailureHandlingOptions(options);
                    trans.Start(joinOrUnjoin);
                    foreach (Element elem in element)
                    {
                        // 找到選取元件的輪廓線
                        BoundingBoxXYZ bb = elem.get_BoundingBox(doc.ActiveView);
                        Outline outline = new Outline(bb.Min, bb.Max);

                        // 創建BoundingBoxIntersectsFilter找到其他與之交接的元件
                        BoundingBoxIntersectsFilter bbFilter = new BoundingBoxIntersectsFilter(outline);
                        // 只獲取可見視圖的元件
                        FilteredElementCollector bbColl = new FilteredElementCollector(doc, doc.ActiveView.Id);
                        // 排除點選元件本身
                        ICollection<ElementId> idsExclude = new List<ElementId>();
                        idsExclude.Add(elem.Id);
                        // 存放到容器內, 兩個都是快篩, 所以順序不重要  
                        ICollection<Element> bbElems = new List<Element>();
                        if (tNsChecked.Count.Equals(4))
                        {
                            bbElems = bbColl.Excluding(idsExclude).WherePasses(bbFilter).WhereElementIsNotElementType().ToElements();
                        }
                        else
                        {
                            foreach (string chooseCategory in tNsChecked)
                            {
                                IList<Element> elems = new List<Element>();
                                bbColl = new FilteredElementCollector(doc, doc.ActiveView.Id); // 清空儲存
                                if (chooseCategory.Equals("柱")) // 柱+結構柱
                                {
                                    ElementCategoryFilter columns = new ElementCategoryFilter(BuiltInCategory.OST_Columns);
                                    ElementCategoryFilter sColumns = new ElementCategoryFilter(BuiltInCategory.OST_StructuralColumns);
                                    LogicalOrFilter columnsFilter = new LogicalOrFilter(sColumns, columns);
                                    elems = bbColl.WherePasses(columnsFilter).Excluding(idsExclude).WherePasses(bbFilter).WhereElementIsNotElementType().ToElements();
                                    foreach (Element e in elems)
                                    {
                                        bbElems.Add(e);
                                    }
                                }
                                else if (chooseCategory.Equals("樑")) // 樑
                                {
                                    elems = bbColl.OfCategory(BuiltInCategory.OST_StructuralFraming).Excluding(idsExclude).WherePasses(bbFilter).WhereElementIsNotElementType().ToElements();
                                    foreach (Element e in elems)
                                    {
                                        bbElems.Add(e);
                                    }
                                }
                                else if (chooseCategory.Equals("牆")) // 牆
                                {
                                    elems = bbColl.OfCategory(BuiltInCategory.OST_Walls).Excluding(idsExclude).WherePasses(bbFilter).WhereElementIsNotElementType().ToElements();
                                    foreach (Element e in elems)
                                    {
                                        bbElems.Add(e);
                                    }
                                }
                                else if (chooseCategory.Equals("板")) // 板
                                {
                                    elems = bbColl.OfCategory(BuiltInCategory.OST_Floors).Excluding(idsExclude).WherePasses(bbFilter).WhereElementIsNotElementType().ToElements();
                                    foreach (Element e in elems)
                                    {
                                        bbElems.Add(e);
                                    }
                                }
                            }
                        }

                        foreach (Element interElem in bbElems)
                        {
                            try
                            {
                                if (joinOrUnjoin.Equals("取消接合"))
                                {
                                    JoinGeometryUtils.UnjoinGeometry(doc, elem, interElem);
                                }
                                else
                                {
                                    JoinGeometryUtils.JoinGeometry(doc, elem, interElem);
                                    if(elem is Floor)
                                    {
                                        JoinGeometryUtils.SwitchJoinOrder(doc, elem, interElem);
                                    }
                                    else if (interElem is Floor || interElem is Ceiling)
                                    {
                                        JoinGeometryUtils.SwitchJoinOrder(doc, elem, interElem);
                                    }
                                }
                            }
                            catch (ArgumentException)
                            {

                            }
                            catch (Exception)
                            {

                            }
                        }
                    }

                    trans.Commit();
                }
            }
            catch (Exception)
            {

            }
        }
        // 取得外部參考Solid交錯的管道
        private void GetIntersectingLinkedElementIds(Solid insSolid, Solid symSolid, IList<RevitLinkInstance> links)
        {
            foreach (RevitLinkInstance i in links)
            {
                // GetTransform or GetTotalTransform or what?
                Transform transform = i.GetTransform();
                if (!transform.AlmostEqual(Transform.Identity))
                {
                    insSolid = SolidUtils.CreateTransformed(insSolid, transform.Inverse);
                }
                ElementIntersectsSolidFilter intersectSolidFilter = new ElementIntersectsSolidFilter(insSolid);
                FilteredElementCollector pipeColl = new FilteredElementCollector(i.GetLinkDocument());
                IList<Element> elems = pipeColl.WherePasses(intersectSolidFilter).WhereElementIsNotElementType().ToElements();
                foreach (Element elem in elems)
                {
                    if (elem is FamilyInstance)
                    {
                        FamilyInstance familyIns = elem as FamilyInstance;
                    }
                }
            }
        }

        // 父層級勾選, 子層級全選
        private void TreeView1_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // 檢查狀態變更時, 才會執行
            if (e.Action != TreeViewAction.Unknown)
            {
                if (e.Node.Nodes.Count > 0)
                {
                    // 傳入檢查狀態已變更的TreeNode當前Checked值
                    this.CheckAllChildNodes(e.Node, e.Node.Checked);
                }
            }
            // 儲存選取Element的名稱與ID
            if (e.Node.Checked)
            {
                if (e.Node.Level.Equals(1))
                {
                    // 儲存上層與被選取的節點名稱
                    checkedNodesList.Add(e.Node.Parent.Text + ":" + e.Node.Text);
                }
            }
            else
            {
                if (e.Node.Level.Equals(1))
                {
                    checkedNodesList.Remove(e.Node.Parent.Text + ":" + e.Node.Text);
                    e.Node.Parent.Checked = false;
                }
            }
        }
        private void CheckAllChildNodes(TreeNode treeNode, bool nodeChecked)
        {
            foreach (TreeNode node in treeNode.Nodes)
            {
                node.Checked = nodeChecked;
                if (node.Nodes.Count > 0)
                {
                    // 如果當前節點有子節點, 則遞迴使用CheckAllChildNodes
                    this.CheckAllChildNodes(node, nodeChecked);
                }
            }
        }
        // 關閉警示視窗
        public class MyPreProcessor : IFailuresPreprocessor
        {
            FailureProcessingResult IFailuresPreprocessor.PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                String transactionName = failuresAccessor.GetTransactionName();
                IList<FailureMessageAccessor> fmas = failuresAccessor.GetFailureMessages();
                if (fmas.Count == 0)
                {
                    return FailureProcessingResult.Continue;
                }
                if (transactionName.Equals("EXEMPLE"))
                {
                    foreach (FailureMessageAccessor fma in fmas)
                    {
                        if (fma.GetSeverity() == FailureSeverity.Error)
                        {
                            failuresAccessor.DeleteAllWarnings();
                            return FailureProcessingResult.ProceedWithRollBack;
                        }
                        else
                        {
                            failuresAccessor.DeleteWarning(fma);
                        }
                    }
                }
                else
                {
                    foreach (FailureMessageAccessor fma in fmas)
                    {
                        failuresAccessor.DeleteAllWarnings();
                    }
                }

                return FailureProcessingResult.Continue;
            }
        }
    }
}