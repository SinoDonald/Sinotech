using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Sinotech_2025
{
    public partial class ManualTagForm : Form
    {
        public bool trueOrFalse = false;
        public int number = 1;
        public ManualTagForm()
        {
            InitializeComponent();
            CenterToParent();
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = true;
            number = Convert.ToInt32(numberTB.Text);
            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
        // 限制TextBox 只能輸入數字，以及限制不能使用快速鍵
        private void OnlyNumber_KeyPress(object sender, KeyPressEventArgs e)
        {
            // e.KeyChar == (Char)48 ~ 57 -----> 0~9
            // e.KeyChar == (Char)8 -----------> Backpace
            // e.KeyChar == (Char)13-----------> Enter
            if (e.KeyChar == (Char)48 || e.KeyChar == (Char)49 ||
               e.KeyChar == (Char)50 || e.KeyChar == (Char)51 ||
               e.KeyChar == (Char)52 || e.KeyChar == (Char)53 ||
               e.KeyChar == (Char)54 || e.KeyChar == (Char)55 ||
               e.KeyChar == (Char)56 || e.KeyChar == (Char)57 ||
               e.KeyChar == (Char)13 || e.KeyChar == (Char)8)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }
    }
}
