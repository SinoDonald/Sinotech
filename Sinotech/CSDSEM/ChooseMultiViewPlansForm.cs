using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sinotech.CSDSEM
{
    public partial class ChooseMultiViewPlansForm : System.Windows.Forms.Form
    {
        public enum FormMode
        {
            AutoPipeTag, // 自動標籤模式（顯示長度設定）
            TagArray     // 標籤排序模式（隱藏長度設定，顯示自動/手動單選鈕）
        }

        /// <summary>自訂ListView滾輪只有上下滑動</summary>
        public class NativeMethods
        {
            public const int GWL_STYLE = -16;
            public const int WS_HSCROLL = 0x00100000;
            [DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        }
        private void HideHorizontalScrollBar(ListView listView)
        {
            int style = NativeMethods.GetWindowLong(listView.Handle, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(listView.Handle, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_HSCROLL);
        }

        Document revitDoc = null;
        public List<ViewPlan> revitViewPlans = new List<ViewPlan>();
        public List<ViewPlan> checkViewPlans = new List<ViewPlan>();
        public double maxM = 10.0;
        public double minM = 2.0;

        private bool HasValidViews = false;
        private FormMode _currentMode;

        // 【新增控制項與回傳屬性】
        private System.Windows.Forms.Panel modePanel;
        private System.Windows.Forms.RadioButton autoRbtn;
        private System.Windows.Forms.RadioButton manualRbtn;
        /// <summary>回傳使用者選擇的是自動(true)還是手動(false)</summary>
        public bool IsAutoResult { get; private set; } = true;

        public ChooseMultiViewPlansForm(Document doc, List<ViewPlan> viewPlans, FormMode mode)
        {
            revitDoc = doc;
            revitViewPlans = viewPlans;
            _currentMode = mode;

            InitializeComponent();

            // 如果是 TagArray 模式，初始化單選按鈕
            if (_currentMode == FormMode.TagArray)
            {
                InitializeRadioButtons();
            }

            // 控制控制項顯示/隱藏
            ToggleLengthControls(mode == FormMode.AutoPipeTag);

            if (viewPlans == null || viewPlans.Count == 0)
            {
                HasValidViews = false;
            }
            else
            {
                HasValidViews = true;
                CreateListView(viewPlans);

                viewplansLV.View = System.Windows.Forms.View.Details;
                foreach (ColumnHeader column in viewplansLV.Columns)
                {
                    column.Width = viewplansLV.ClientSize.Width / viewplansLV.Columns.Count;
                }
                HideHorizontalScrollBar(viewplansLV);
            }
            CenterToParent();
        }

        // =========================================================

        /// <summary>【修正】動態初始化單選按鈕，並使用 Panel 隔離群組</summary>
        private void InitializeRadioButtons()
        {
            // 建立一個 Panel 當作隔離容器
            this.modePanel = new System.Windows.Forms.Panel();
            this.modePanel.Location = new System.Drawing.Point(12, 320); // 調整至原本的左下角位置
            this.modePanel.Size = new System.Drawing.Size(150, 30);      // 給予足夠的寬高容納按鈕

            this.autoRbtn = new System.Windows.Forms.RadioButton();
            this.manualRbtn = new System.Windows.Forms.RadioButton();

            // 自動 RadioButton (座標相對於 modePanel 容器)
            this.autoRbtn.AutoSize = true;
            this.autoRbtn.Location = new System.Drawing.Point(0, 3);
            this.autoRbtn.Name = "autoRbtn";
            this.autoRbtn.Size = new System.Drawing.Size(51, 21);
            this.autoRbtn.Text = "自動";
            this.autoRbtn.UseVisualStyleBackColor = true;

            // 手動 RadioButton (座標相對於 modePanel 容器)
            this.manualRbtn.AutoSize = true;
            this.manualRbtn.Location = new System.Drawing.Point(70, 3);
            this.manualRbtn.Name = "manualRbtn";
            this.manualRbtn.Size = new System.Drawing.Size(51, 21);
            this.manualRbtn.Text = "手動";
            this.manualRbtn.Checked = true; // 預設手動
            this.manualRbtn.UseVisualStyleBackColor = true;

            // 將兩個按鈕先裝進 Panel
            this.modePanel.Controls.Add(this.autoRbtn);
            this.modePanel.Controls.Add(this.manualRbtn);

            // 最後再將 Panel 裝進主視窗
            this.Controls.Add(this.modePanel);
        }

        /// <summary>控制下方的 UI 顯示隱藏</summary>
        private void ToggleLengthControls(bool visible)
        {
            label1.Visible = visible;   
            textBox1.Visible = visible; 
            label2.Visible = visible;   
            label4.Visible = visible;   
            textBox2.Visible = visible; 
            label3.Visible = visible;   

            // 既然按鈕都裝進 Panel 了，只要控制 Panel 的顯示/隱藏即可！
            if (this.modePanel != null)
            {
                this.modePanel.Visible = !visible;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!HasValidViews)
            {
                Autodesk.Revit.UI.TaskDialog.Show("提示", "該視圖類型下，沒有符合「出圖」且「無子視圖」條件的平面圖！");
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }

        private void CreateListView(List<ViewPlan> viewPlans)
        {
            viewplansLV.Items.Clear();
            foreach (ViewPlan viewPlan in viewPlans.OrderBy(x => x.Origin.Z).ThenBy(x => x.Name).ToList())
            { viewplansLV.Items.Add(viewPlan.Name); }

            for (int i = 0; i < viewplansLV.Items.Count; i++)
            {
                viewplansLV.Items[i].Checked = true;
            }
            viewplansLV.View = System.Windows.Forms.View.List;
        }

        private void viewplanLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListView selectListView = sender as ListView;
            ListViewItem focusedItem = selectListView.FocusedItem;
            if (selectListView.SelectedItems.Count > 0)
            {
                if (focusedItem.Checked == true) focusedItem.Checked = false;
                else focusedItem.Checked = true;
            }
        }

        private void allRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < viewplansLV.Items.Count; i++) viewplansLV.Items[i].Checked = true;
        }

        private void allCancelRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < viewplansLV.Items.Count; i++) viewplansLV.Items[i].Checked = false;
        }

        private void sureBtn_Click(object sender, EventArgs e)
        {
            try
            {
                this.checkViewPlans = new List<ViewPlan>();
                foreach (ListViewItem listView in viewplansLV.CheckedItems)
                {
                    if (listView != null)
                    {
                        ViewPlan checkViewPlan = revitViewPlans.Where(x => x.Name.Equals(listView.Text)).FirstOrDefault();
                        if (checkViewPlan != null) { this.checkViewPlans.Add(checkViewPlan); }
                    }
                }
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

            if (this.checkViewPlans.Count == 0)
            {
                MessageBox.Show("請至少選擇一個視圖！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 根據模式儲存對應的結果
            if (_currentMode == FormMode.AutoPipeTag)
            {
                this.maxM = Convert.ToDouble(textBox1.Text);
                this.minM = Convert.ToDouble(textBox2.Text);
            }
            else if (_currentMode == FormMode.TagArray)
            {
                this.IsAutoResult = autoRbtn.Checked; // 紀錄自動或手動
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void cancelBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            TextBox txt = sender as TextBox;
            if (char.IsDigit(e.KeyChar) || char.IsControl(e.KeyChar)) return;
            if (e.KeyChar == '-' || e.KeyChar == '+')
            {
                if (txt.SelectionStart == 0 && !txt.Text.Contains("-") && !txt.Text.Contains("+")) return;
            }
            if (e.KeyChar == '.')
            {
                if (txt.Text.Contains(".")) { e.Handled = true; return; }
                if (txt.SelectionStart > 0)
                {
                    char prevChar = txt.Text[txt.SelectionStart - 1];
                    if (char.IsDigit(prevChar)) return;
                }
            }
            e.Handled = true;
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            TextBox txt = sender as TextBox;
            if (!string.IsNullOrEmpty(txt.Text) && txt.Text != "-" && txt.Text != "+" && !double.TryParse(txt.Text, out _))
            {
                if (txt.Text.Length > 0)
                {
                    txt.Text = txt.Text.Remove(txt.Text.Length - 1);
                    txt.SelectionStart = txt.Text.Length;
                }
            }
        }
    }
}