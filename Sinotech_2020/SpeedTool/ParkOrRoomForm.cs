using System;
using System.Windows.Forms;

namespace Sinotech_2020.SpeedTool
{
    public partial class ParkOrRoomForm : Form
    {
        public string parkOrRoom = string.Empty;
        public string before = string.Empty;
        public string textBoxNum = string.Empty;
        public string behind = string.Empty;
        public bool yesOrNo = false;
        public bool noFour = false;
        public bool changeFour = false;
        public string changeSign = "";

        public ParkOrRoomForm()
        {
            InitializeComponent();
            parkOrRoomCB.Text = parkOrRoomCB.Items[0].ToString(); // 預設停車格            
            replaceNumber.ReadOnly = true; // 數值4取代預設唯讀

            // 畫面置中
            CenterToScreen();
        }

        // 確定
        private void SureBtn_Click(object sender, EventArgs e)
        {
            yesOrNo = true;

            try
            {                
                parkOrRoom = parkOrRoomCB.Text; // 選擇停車格或房間
                before = beforeName.Text; // 前綴                
                textBoxNum = startNumber.Text; // 起始編號                
                behind = behindName.Text; // 後綴
                if (noFourRB.Checked == true) // 是否略過尾數4
                {
                    noFour = true;
                }                
                else if (replaceFourRB.Checked == true) // 尾數4取代
                {
                    changeFour = true;
                    changeSign = replaceNumber.Text;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            Close();
        }
        // 取消
        private void CancelBtn_Click(object sender, EventArgs e)
        {
            yesOrNo = false;

            Close();
        }
        // 點選略過尾數4時, 不得輸入取代內容
        private void NoFourRB_CheckedChanged(object sender, EventArgs e)
        {
            replaceNumber.ReadOnly = true;

        }
        // 點選尾數4取代時, 可輸入取代內容
        private void ReplaceFourRB_CheckedChanged(object sender, EventArgs e)
        {
            replaceNumber.ReadOnly = false;
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
