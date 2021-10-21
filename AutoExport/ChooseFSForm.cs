using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AutoExport
{
    public partial class ChooseFSForm : System.Windows.Forms.Form
    {
        List<FamilySymbol> familySymbolList = new List<FamilySymbol>();
        public FamilySymbol familySymbol = null;
        RadioButton[] radioButtons = new RadioButton[] { };
        public static bool trueOrFalse = false; // 確定或取消

        public ChooseFSForm()
        {
            InitializeComponent();

            CreateRadioButton(); // 新增RadioButton

            CenterToScreen();
        }
        // 新增RadioButton
        private void CreateRadioButton()
        {
            this.familySymbolList = CopyDrawings.familySymbolList;
            this.radioButtons = new RadioButton[familySymbolList.Count];
            for (int i = 0; i < familySymbolList.Count; i++)
            {
                radioButtons[i] = new RadioButton();
                radioButtons[i].Font = new Font("微軟正黑體", 10, FontStyle.Regular);
                radioButtons[i].Text = familySymbolList[i].FamilyName + "_" + familySymbolList[i].Name;
                radioButtons[i].AutoSize = true;
                radioButtons[i].Location = new System.Drawing.Point(5, 5 + i * 25);
                radioBtnPanel.Controls.Add(radioButtons[i]);
                if(i == 0)
                {
                    radioButtons[0].Checked = true; // 預設第一個
                }
            }
        }
        // 確定
        private void sure_Click(object sender, EventArgs e)
        {
            trueOrFalse = true;
            // 讀取所有Panel中的RadioButton
            foreach (System.Windows.Forms.Control control in radioBtnPanel.Controls)
            {
                RadioButton rb = control as RadioButton;
                if (rb != null && rb.Checked)
                {
                    // 找到familySymbolList中相符的familySymbol並回傳
                    this.familySymbol = (from x in this.familySymbolList
                                         where x.Name.Equals(rb.Text)
                                         select x).FirstOrDefault();
                    break;
                }
            }
            Close();
        }
        // 取消
        private void cancel_Click(object sender, EventArgs e)
        {
            trueOrFalse = false;
            Close();
        }
    }
}
