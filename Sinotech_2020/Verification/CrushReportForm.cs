using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using static Sinotech_2020.Verification.CrushReport;

namespace Sinotech_2020.Verification
{
    public partial class CrushReportForm : System.Windows.Forms.Form
    {
        UIDocument formUIdoc = null;
        Document formDoc = null;
        Element elem = null;
        List<string> checkedNodesList = new List<string>(); // 儲存選取的節點
        public CrushReportForm(UIDocument uidoc, Document doc)
        {
            InitializeComponent();
            this.formUIdoc = uidoc;
            this.formDoc = doc;

            List<ElemInfo> elemInfoList = CrushReport.elemInfoList;
            var cgList = (from x in elemInfoList
                          select x.categoryName).Distinct();
            int i = 0;
            foreach (string cg in cgList)
            {
                treeView1.Nodes.Add(cg);
                var familyNames = (from x in elemInfoList
                                   where x.categoryName.Equals(cg)
                                   select x.familyName).Distinct();
                int j = 0;
                foreach (string familyName in familyNames)
                {
                    treeView1.Nodes[i].Nodes.Add(familyName);
                    var elemList = (from x in elemInfoList
                                    where x.categoryName.Equals(cg) && x.familyName.Equals(familyName)
                                    select x);
                    foreach (var elem in elemList)
                    {
                        treeView1.Nodes[i].Nodes[j].Nodes.Add(elem.name + "：" + elem.elemId);
                    }
                    j++;
                }
                i++;
            }
            CenterToParent();
        }
        // 單擊亮顯選取元件
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode.Level.Equals(2))
            {
                try
                {
                    string[] content = treeView1.SelectedNode.Text.Split('：');
                    ElementId id = new ElementId(Convert.ToInt32(content[1]));
                    elem = formDoc.GetElement(id);
                    ICollection<ElementId> ids = new List<ElementId>();
                    ids.Add(id);
                    this.formUIdoc.Selection.SetElementIds(ids);
                }
                catch (System.IndexOutOfRangeException)
                {

                }
            }
        }
        // 雙擊顯示碰撞元件
        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            if (treeView1.SelectedNode.Level.Equals(2))
            {
                try
                {
                    string[] content = treeView1.SelectedNode.Text.Split('：');
                    ElementId id = new ElementId(Convert.ToInt32(content[1]));
                    elem = formDoc.GetElement(id);
                    ElementIntersectsElementFilter interFilter = new ElementIntersectsElementFilter(elem);
                    FilteredElementCollector interCollector = new FilteredElementCollector(formDoc);
                    ICollection<Element> interElemList = interCollector.WherePasses(interFilter).WhereElementIsNotElementType().ToElements();
                    string crushReport = "名稱：" + elem.Name + "_ ID : " + elem.Id + "\r\n";
                    if (interElemList.Count.Equals(0))
                    {
                        crushReport += "未偵測到干涉\r\n";
                    }
                    else
                    {
                        int i = 1;
                        foreach (Element interElem in interElemList)
                        {
                            crushReport += i + " : 名稱 : " + interElem.Name + "_ ID : " + interElem.Id + "\r\n";
                            i++;
                        }
                    }
                    TaskDialog.Show("Revit", crushReport);
                }
                catch (System.IndexOutOfRangeException)
                {

                }
            }
        }
        // 父層級勾選, 子層級全選
        private void treeView1_AfterCheck(object sender, TreeViewEventArgs e)
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
                if (e.Node.Level.Equals(2))
                {
                    checkedNodesList.Add(e.Node.Text);
                }
            }
            else
            {
                if (e.Node.Level.Equals(2))
                {
                    checkedNodesList.Remove(e.Node.Text);
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
        // 確定
        private void sure_Click(object sender, EventArgs e)
        {
            string crushReport = string.Empty;
            foreach (string checkedNodes in checkedNodesList)
            {
                string[] nameId = checkedNodes.Split('：');
                string name = nameId[0];
                ElementId id = new ElementId(Convert.ToInt32(nameId[1]));
                Element nodeElem = formDoc.GetElement(id);
                crushReport += CrushReportInfo(nodeElem);
            }
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            DateTime dt = DateTime.Now;
            string now = dt.Year.ToString() + dt.Month + dt.Day + "_" + dt.Hour + dt.Minute +dt.Second;
            System.IO.File.WriteAllText(desktopPath + "\\干涉報告_" + now + ".txt", crushReport);
            Close();
            TaskDialog.Show("Revit", "輸出完成");
        }
        // 衝突報告資訊
        private string CrushReportInfo(Element nodeElem)
        {
            ElementIntersectsElementFilter interFilter = new ElementIntersectsElementFilter(nodeElem);
            FilteredElementCollector interCollector = new FilteredElementCollector(formDoc);
            ICollection<Element> interElemList = interCollector.WherePasses(interFilter).WhereElementIsNotElementType().ToElements();
            string crushReport = "名稱：" + nodeElem.Name + "_ ID : " + nodeElem.Id + "\r\n";
            if (interElemList.Count.Equals(0))
            {
                crushReport += "未偵測到干涉\r\n";
            }
            else
            {
                int i = 1;
                foreach (Element interElem in interElemList)
                {
                    crushReport += i + " : 名稱 : " + interElem.Name + "_ ID : " + interElem.Id + "\r\n";
                    i++;
                }
            }
            crushReport += "********************************************" + "\r\n\r\n";
            return crushReport;
        }
        // 取消
        private void cancel_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
