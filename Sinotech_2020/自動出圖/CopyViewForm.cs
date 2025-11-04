using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using static Sinotech_2020.CopyDrawings;

namespace Sinotech_2020
{
    public partial class CopyViewForm : System.Windows.Forms.Form
    {
        public static List<ViewInfo> viewInfoList = new List<ViewInfo>();
        List<string> checkedNodesList = new List<string>(); // 儲存選取的節點
        Document formDoc = null;

        public CopyViewForm(Document doc)
        {
            InitializeComponent();
            this.formDoc = doc;

            this.comboBox1.Text = this.comboBox1.Items[0].ToString(); // 複製
            CreateNodes(); // 新增節點
            //treeView1.ExpandAll(); // 全部展開

            CenterToScreen();
        }
        // 新增節點
        private void CreateNodes()
        {
            viewInfoList = CopyDrawings.viewInfoList;
            // 不同的ViewFamilyType名稱
            var vftNames = (from x in viewInfoList
                            orderby x.vftName
                            select x.vftName).Distinct();
            int i = 0;
            foreach (var vftName in vftNames)
            {
                treeView1.Nodes.Add(vftName);
                // 各個ViewFamilyType的樓層名稱, 依照LevelId排序
                var viewInfos = (from x in viewInfoList
                                 where x.vftName.Equals(vftName)
                                 select x).OrderBy(x => x.vftName).ThenBy(x => x.name).Distinct();
                {
                    foreach (var viewInfo in viewInfos)
                    {
                        if (!viewInfo.name.Contains(" 複製 ") && !viewInfo.name.Contains(" Copy ") && !viewInfo.name.Contains(" 從屬 ")) // 複製與從屬的視圖不列出選擇
                        {
                            treeView1.Nodes[i].Nodes.Add(viewInfo.name);
                        }
                    }
                    i++;
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
            foreach (string checkedNodes in checkedNodesList)
            {
                List<Autodesk.Revit.DB.View> viewList = new List<Autodesk.Revit.DB.View>();
                List<int> copyCountList = new List<int>();
                copyCountList.Add(0);
                var createViews = (from x in viewInfoList
                                   where (x.vftName + ":" + x.name).Equals(checkedNodes)
                                   select x.view);
                foreach(var view in createViews)
                {
                    if(view.Name.Contains(" 複製 "))
                    {
                        try
                        {
                            string copy = " 複製 ";
                            int viewCopyCount = Convert.ToInt32(view.Name.LastIndexOf(copy) + copy.Length);
                            int number = Convert.ToInt32(view.Name.Substring(viewCopyCount, view.Name.Length - viewCopyCount));
                            copyCountList.Add(number);
                        }
                        catch (Exception) // 避免" 複製 "後有非數值
                        {

                        }
                    }
                    else
                    {
                        viewList.Add(view);
                    }
                }
                try
                {
                    ViewDuplicateOption viewDuplicateOption = ViewDuplicateOption.Duplicate; // 複製
                    if (this.comboBox1.Text.Equals("與細節一起複製"))
                    {
                        viewDuplicateOption = ViewDuplicateOption.WithDetailing;
                    }
                    else if (this.comboBox1.Text.Equals("複製為從屬視圖"))
                    {
                        viewDuplicateOption = ViewDuplicateOption.AsDependent;
                    }
                    int start = copyCountList.Max() + 1; // 複製起始值
                    int count = Convert.ToInt32(textBox1.Text); // 複製數量
                    int end = start + count;
                    CreateViewPlan(formDoc, viewList, viewDuplicateOption, start, end); // 新增ViewPlan
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Revit", ex.Message);
                }
            }

            Close();
        }
        // 取消
        private void cancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        // 限制TextBox 只能輸入數字，以及限制不能使用快速鍵
        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)Keys.Back || e.KeyChar == (char)Keys.Enter)
            {
                return;
            }
            if (e.KeyChar == '.')
            {
                //判定textBox1是否有小數點
                foreach (char i in textBox1.Text)
                {
                    if (i == '.')
                    {
                        e.Handled = true;
                    }
                }
                return;
            }

            if (e.KeyChar < '0' || e.KeyChar > '9')
            {
                e.Handled = true;
            }
        }
    }
}
