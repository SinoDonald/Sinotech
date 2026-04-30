using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Form = System.Windows.Forms.Form;
using View = Autodesk.Revit.DB.View;

namespace Sinotech.CSDSEM
{
    public partial class ViewSelectionForm : Form
    {
        private TreeView treeView1;
        private Button btnOk;
        private Button btnCancel;

        // 用來存放使用者最後勾選的視圖
        public List<View> SelectedViews { get; private set; }

        // 建構子：接收三層架構的視圖字典
        public ViewSelectionForm(Dictionary<string, Dictionary<string, List<View>>> groupedViews)
        {
            InitializeComponent();
            SelectedViews = new List<View>();
            PopulateTreeView(groupedViews);
        }

        // 將傳入的資料長成三層樹狀結構
        private void PopulateTreeView(Dictionary<string, Dictionary<string, List<View>>> groupedViews)
        {
            treeView1.Nodes.Clear();

            // 1. 遍歷第一層 (例如："???", "Key Plan", "出圖")
            foreach (var topGroup in groupedViews)
            {
                TreeNode topNode = new TreeNode(topGroup.Key);
                topNode.Tag = "TopGroup"; // 標記為第一層

                // 2. 遍歷第二層 (例如："樓板平面圖", "結構平面")
                foreach (var subGroup in topGroup.Value)
                {
                    TreeNode subNode = new TreeNode(subGroup.Key);
                    subNode.Tag = "SubGroup"; // 標記為第二層

                    // 3. 遍歷第三層 (實際的視圖)
                    foreach (View view in subGroup.Value)
                    {
                        TreeNode childNode = new TreeNode(view.Name);
                        childNode.Tag = view; // 把 Revit View 物件藏在 Tag 裡
                        subNode.Nodes.Add(childNode);
                    }

                    // 如果這個第二層分類底下有視圖，才把它加到第一層底下
                    if (subNode.Nodes.Count > 0)
                    {
                        topNode.Nodes.Add(subNode);
                    }
                }

                // 如果這個第一層分類底下有內容，才把它加到 TreeView
                if (topNode.Nodes.Count > 0)
                {
                    treeView1.Nodes.Add(topNode);
                }
            }

            treeView1.ExpandAll(); // 預設展開所有節點
        }
        /// <summary>
        /// 處理母節點、子節點的勾選連動邏輯，當點擊「核取方塊」時觸發
        /// <summary>
        private void TreeView1_AfterCheck(object sender, TreeViewEventArgs e)
        {
            // 【關鍵】Action != Unknown 代表這是滑鼠或鍵盤真實操作方塊，而非被程式碼改變
            if (e.Action != TreeViewAction.Unknown)
            {
                SyncNodeCheckState(e.Node);
            }
        }
        /// <summary>
        /// 讓使用者點擊節點「文字」時，也能切換勾選狀態
        /// </summary>
        private void TreeView1_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                TreeViewHitTestInfo hitInfo = treeView1.HitTest(e.Location);

                if (hitInfo.Location == TreeViewHitTestLocations.Label)
                {
                    // 1. 切換狀態 
                    // (這行會觸發 AfterCheck，但因為是被程式改變的，會被上面那個 Unknown 判斷擋掉，很安全！)
                    e.Node.Checked = !e.Node.Checked;

                    // 2. 手動呼叫連動邏輯
                    SyncNodeCheckState(e.Node);
                }
            }
        }
        /// <summary>
        /// 獨立出來的核心邏輯：處理母子節點的勾選連動
        /// </summary>
        private void SyncNodeCheckState(TreeNode node)
        {
            // 暫時解除事件，避免無限迴圈
            treeView1.AfterCheck -= TreeView1_AfterCheck;

            // 1. 如果有子節點 (代表點到的是母節點)，子節點跟著連動
            if (node.Nodes.Count > 0)
            {
                foreach (TreeNode child in node.Nodes)
                {
                    child.Checked = node.Checked;
                }
            }

            // 2. 如果有母節點 (代表點到的是子節點)，檢查是否要改變母節點狀態
            if (node.Parent != null)
            {
                bool allChecked = true;
                foreach (TreeNode sibling in node.Parent.Nodes)
                {
                    if (!sibling.Checked)
                    {
                        allChecked = false;
                        break;
                    }
                }
                node.Parent.Checked = allChecked;
            }

            // 恢復事件綁定
            treeView1.AfterCheck += TreeView1_AfterCheck;
        }
        private void BtnOk_Click(object sender, EventArgs e)
        {
            // 收集所有被打勾的【子節點】裡的 View 物件
            foreach (TreeNode parentNode in treeView1.Nodes)
            {
                foreach (TreeNode childNode in parentNode.Nodes)
                {
                    if (childNode.Checked && childNode.Tag is View view)
                    {
                        SelectedViews.Add(view);
                    }
                }
            }

            if (SelectedViews.Count == 0)
            {
                MessageBox.Show("請至少選擇一個視圖！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}