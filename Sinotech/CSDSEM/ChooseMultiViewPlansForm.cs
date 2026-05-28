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
        /// <summary>
        /// 自訂ListView滾輪只有上下滑動
        /// </summary>
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
        public double maxM = 10.0; // 大於此長度必標
        public double minM = 2.0; // 小於此長度不標
        public bool trueOrFalse = false;

        // 【新增】用來記錄 ListView 是否為空
        private bool HasValidViews = false;

        public ChooseMultiViewPlansForm(Document doc, List<ViewPlan> viewPlans)
        {
            revitDoc = doc;
            revitViewPlans = viewPlans;
            InitializeComponent();

            // =========================================================
            // 【第二道防線】：如果傳進來的視圖清單是 0
            // =========================================================
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
                HideHorizontalScrollBar(viewplansLV); // 自訂ListView滾輪只有上下滑動
            }
            CenterToParent();
        }

        // =========================================================
        // 【新增機制】在表單即將顯示前，如果發現 ListView 為 0，跳出提醒並直接關閉
        // =========================================================
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!HasValidViews)
            {
                // 注意：這裡使用 Autodesk.Revit.UI 呼叫原生的 TaskDialog
                Autodesk.Revit.UI.TaskDialog.Show("提示", "該視圖類型下，沒有符合「出圖」且「無子視圖」條件的平面圖！");
                this.DialogResult = DialogResult.Cancel;
                this.Close(); // 安全阻斷，表單不會閃現出來
            }
        }
        // 新增ListView
        private void CreateListView(List<ViewPlan> viewPlans)
        {
            viewplansLV.Items.Clear();
            foreach (ViewPlan viewPlan in viewPlans.OrderBy(x => x.Origin.Z).ThenBy(x => x.Name).ToList())
            { viewplansLV.Items.Add(viewPlan.Name); }
            // 預設全選
            for (int i = 0; i < viewplansLV.Items.Count; i++)
            {
                viewplansLV.Items[i].Checked = true;
            }
            viewplansLV.View = System.Windows.Forms.View.List;
        }
        // 點選ListView item文字即可勾選或取消
        private void viewplanLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListView selectListView = sender as ListView;
            ListViewItem focusedItem = selectListView.FocusedItem;
            if (selectListView.SelectedItems.Count > 0)
            {
                if (focusedItem.Checked == true)
                {
                    focusedItem.Checked = false;
                }
                else
                {
                    focusedItem.Checked = true;
                }
            }
        }
        // 全選
        private void allRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < viewplansLV.Items.Count; i++)
            {
                viewplansLV.Items[i].Checked = true;
            }
        }
        // 全部取消
        private void allCancelRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < viewplansLV.Items.Count; i++)
            {
                viewplansLV.Items[i].Checked = false;
            }
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            try
            {
                this.checkViewPlans = new List<ViewPlan>();
                foreach (ListViewItem listView in viewplansLV.CheckedItems)
                {
                    try
                    {
                        if(listView != null)
                        {
                            ViewPlan checkViewPlan = revitViewPlans.Where(x => x.Name.Equals(listView.Text)).FirstOrDefault();
                            if (checkViewPlans != null) { this.checkViewPlans.Add(checkViewPlan); }
                        }
                    }
                    catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                }
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

            if (this.checkViewPlans.Count == 0)
            {
                MessageBox.Show("請至少選擇一個視圖！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            this.maxM = Convert.ToDouble(textBox1.Text); // 大於此長度必標
            this.minM = Convert.ToDouble(textBox2.Text); // 小於此長度不標
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
        // 限制TextBox 只能輸入數字，以及限制不能使用快速鍵
        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            TextBox txt = sender as TextBox;

            // 1. 允許數字與 Backspace 等控制鍵
            if (char.IsDigit(e.KeyChar) || char.IsControl(e.KeyChar))
            {
                return;
            }

            // 2. 處理正負號 (+ 或 -)
            if (e.KeyChar == '-' || e.KeyChar == '+')
            {
                // 只能出現在索引 0，且目前文字內不能已經有正負號
                if (txt.SelectionStart == 0 && !txt.Text.Contains("-") && !txt.Text.Contains("+"))
                {
                    return;
                }
            }

            // 3. 處理小數點 (.)
            if (e.KeyChar == '.')
            {
                // 已經有小數點了就不能再輸入
                if (txt.Text.Contains("."))
                {
                    e.Handled = true;
                    return;
                }

                // 小數點不能在正負號之後立即出現 (例如輸入了 "-" 之後不能直接點 ".")
                // 或是確保小數點前面至少要有一個數字 (視你的業務需求而定)
                if (txt.SelectionStart > 0)
                {
                    // 檢查游標前一個字元是否為數字
                    char prevChar = txt.Text[txt.SelectionStart - 1];
                    if (char.IsDigit(prevChar))
                    {
                        return;
                    }
                }
            }

            // 4. 其他字元通通攔截
            e.Handled = true;
        }
        // 限制TextBox 只能輸入數字，並處理貼上內容
        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            TextBox txt = sender as TextBox;
            // 嘗試轉換為 double，如果失敗且不是空字串或只有正負號，就還原或提示
            if (!string.IsNullOrEmpty(txt.Text) &&
                txt.Text != "-" && txt.Text != "+" &&
                !double.TryParse(txt.Text, out _))
            {
                // 簡單暴力：如果格式不正確就清除最後一個字元
                if (txt.Text.Length > 0)
                {
                    txt.Text = txt.Text.Remove(txt.Text.Length - 1);
                    txt.SelectionStart = txt.Text.Length; // 保持游標在最後
                }
            }
        }
    }
}