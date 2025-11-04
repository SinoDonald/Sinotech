using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using static Sinotech.CopyDrawings;

namespace Sinotech
{
    public partial class MoveViewForm : System.Windows.Forms.Form
    {
        double oldWidth;
        double oldHeight;
        private List<ViewInfo> viewInfoList = new List<ViewInfo>();
        List<string> checkedNodesList = new List<string>(); // 儲存選取的視圖節點
        string viewSheetName = string.Empty; // 視圖名稱
        UIDocument formUiDoc = null;
        Document formDoc = null;

        public MoveViewForm(UIDocument uidoc, Document doc)
        {
            InitializeComponent();
            this.formUiDoc = uidoc;
            this.formDoc = doc;

            // Step1.紀錄Form本來的大小(長與寬) 
            oldWidth = this.Width;
            oldHeight = this.Height;

            CreateNodes(); // 新增節點
            //treeView1.ExpandAll(); // 全部展開

            CenterToScreen();
        }

        // 新增節點
        private void CreateNodes()
        {
            this.viewInfoList = MoveView.viewInfoList;
            // 不同的ViewFamilyType名稱
            var vftNames = (from x in viewInfoList
                            orderby x.vftName
                            select x.vftName).Distinct();
            int i = 0;
            foreach (var vftName in vftNames)
            {
                if (vftName.Equals("圖紙") || vftName.Equals("Sheet"))
                {
                    treeView2.Nodes.Add(vftName);
                }
                else
                {
                    treeView1.Nodes.Add(vftName);
                }
                // 各個ViewFamilyType的樓層名稱, 依照LevelId排序
                var viewInfos = (from x in viewInfoList
                                 where x.vftName.Equals(vftName)
                                 select x).OrderBy(x => x.vftName).ThenBy(x => x.name).Distinct();
                {

                    if (vftName.Equals("圖紙") || vftName.Equals("Sheet"))
                    {
                        foreach (var viewInfo in viewInfos)
                        {
                            treeView2.Nodes[0].Nodes.Add(viewInfo.name);
                        }
                        treeView2.Nodes[0].Nodes[0].Checked = true;
                    }
                    else
                    {
                        foreach (var viewInfo in viewInfos)
                        {
                            //if (!viewInfo.name.Contains(" 複製 ") && !viewInfo.name.Contains(" Copy ") && !viewInfo.name.Contains(" 從屬 ")) // 複製與從屬的視圖不列出選擇
                            //{
                                treeView1.Nodes[i].Nodes.Add(viewInfo.name);
                            //}
                        }
                        i++;
                    }
                }
            }
        }
        // 確定
        private void sure_Click(object sender, EventArgs e)
        {
            Autodesk.Revit.DB.View viewSheet = (from x in viewInfoList
                                                where x.name.Equals(viewSheetName)
                                                select x.view).FirstOrDefault();

            foreach (string checkedNodes in checkedNodesList)
            {
                var createViews = (from x in viewInfoList
                                   where (x.vftName + ":" + x.name).Equals(checkedNodes)
                                   select x.view);
                foreach (var view in createViews)
                {
                    try
                    {
                        using(Transaction trans = new Transaction(formDoc, "移動視圖"))
                        {
                            trans.Start();
                            // 將視圖放置圖紙中心
                            UV location = new UV((viewSheet.Outline.Max.U - viewSheet.Outline.Min.U) / 2, (viewSheet.Outline.Max.V - viewSheet.Outline.Min.V) / 2);
                            // viewSheet.AddView(view3D, location);
                            //if(Viewport.CanAddViewToSheet(formDoc, viewSheet.Id, view.Id) == true)
                            //{
                                Viewport.Create(formDoc, viewSheet.Id, view.Id, new XYZ(location.U, location.V, 0));
                            //}
                            trans.Commit();
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException) // 視圖已擺放至其他圖紙
                    {
                        TaskDialog.Show("Revit", "視圖 " + view.Title + " 已擺放至其他圖紙");
                    }
                    catch (Exception ex)
                    {
                        TaskDialog.Show("Revit", ex.Message + "\n\n" + ex.ToString());
                    }
                }
            }

            Close();
        }
        // 取消
        private void cancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        // 視圖選擇
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
        // 圖紙選擇
        private void treeView2_AfterSelect(object sender, TreeViewEventArgs e)
        {
            viewSheetName = e.Node.Text;
        }

        // 視窗縮放
        private void MoveViewForm_Resize(object sender, EventArgs e)
        {
            // Step2.計算比例 
            double x = (this.Width / oldWidth);
            double y = (this.Height / oldHeight);

            // Step3.控制項 Resize 
            treeView1.Width = Convert.ToInt32(x * treeView1.Width);
            treeView1.Height = Convert.ToInt32(y * treeView1.Height);
            treeView2.Width = Convert.ToInt32(x * treeView2.Width);
            treeView2.Height = Convert.ToInt32(y * treeView2.Height);

            // Step4.把Form本來大小值設為目前大小值 
            oldWidth = this.Width;
            oldHeight = this.Height;
        }
    }
}
