using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sinotech_2020
{
    public partial class PipeOpeningForm : Form
    {
        public IList<string> FilteredList = new List<string>();
        public CheckedListBox.CheckedItemCollection CheckedItems;
        public PipeOpeningForm()
        {
            InitializeComponent();
            checkedListBox1.SetItemChecked(0, true);
            checkedListBox1.SetItemChecked(1, true);
            CenterToScreen(); // 置中
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            foreach (var item in checkedListBox1.CheckedItems)
            {
                FilteredList.Add(item.ToString());
            }

            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();

        }
    }
}
