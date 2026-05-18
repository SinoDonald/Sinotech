using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Sinotech_2020.CSDSEM
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

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}