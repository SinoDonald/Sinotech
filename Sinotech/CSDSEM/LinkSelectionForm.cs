using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Sinotech.CSDSEM
{
    public partial class LinkSelectionForm : Form
    {
        // 變數名稱改為 SelectedProjects，型別改為 ProjectItem
        public List<ProjectItem> SelectedProjects { get; private set; }

        // 建構子接收的參數改為 ProjectItem 清單
        public LinkSelectionForm(List<ProjectItem> projects)
        {
            InitializeComponent();
            SelectedProjects = new List<ProjectItem>();

            // 加入 CheckedListBox
            foreach (var proj in projects)
            {
                // 如果要預設把主模型打勾，checkedListBox1.Items.Add(proj, isChecked);
                bool isChecked = proj.IsMainModel;
                checkedListBox1.Items.Add(proj, false);
            }

            CenterToParent();
        }

        // 當你點擊「完成」按鈕時觸發的事件
        private void btnOk_Click(object sender, EventArgs e)
        {
            // 取出勾選項目
            foreach (var item in checkedListBox1.CheckedItems)
            {
                SelectedProjects.Add((ProjectItem)item);
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // 當你點擊「取消」按鈕時觸發的事件
        private void btnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}