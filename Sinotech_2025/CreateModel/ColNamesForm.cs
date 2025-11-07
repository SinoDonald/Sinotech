using System;
using System.Windows.Forms;

namespace Sinotech_2025.CreateModel
{
    public partial class ColNamesForm : Form
    {
        public ColNamesForm()
        {
            InitializeComponent();
            foreach(string columnName in AutoColumn.columnNames)
            {
                columnsCB.Items.Add(columnName);
            }
            columnsCB.Text = columnsCB.Items[0].ToString(); // 預設第一種柱類型
            CenterToScreen(); // 畫面置中
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            AutoColumn.familyName = columnsCB.Text; // 柱類型
            AutoColumn.trueOrFalse = true;
            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
