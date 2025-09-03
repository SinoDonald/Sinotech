using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Sinotech_2025
{
    public partial class CreateDrawings : System.Windows.Forms.Form
    {
        public string titleBlocksName = "工程圖核備章"; // 選取的圖框名稱
        public List<string> checkSheets = new List<string>(); // 選取的Sheet名稱
        public static bool trueOrFalse = false; // 確定或取消

        public CreateDrawings()
        {
            InitializeComponent();
            CreateRadioButton(); // 新增RadioButton
            CreateCheckBox(); // 新增CheckBox
            CenterToScreen(); // 置中
        }
        // 新增RadioButton, 從專案中找到全部的圖框
        private void CreateRadioButton()
        {
            RadioButton[] radioButtons = new RadioButton[] { };
            List<FamilySymbol> familySymbolList = Sinotech_API.familySymbolList;
            radioButtons = new RadioButton[familySymbolList.Count];
            for (int i = 0; i < familySymbolList.Count; i++)
            {
                radioButtons[i] = new RadioButton();
                radioButtons[i].Font = new Font("微軟正黑體", 10, FontStyle.Regular);
                if (familySymbolList[i].FamilyName.Equals(familySymbolList[i].Name))
                {
                    radioButtons[i].Text = familySymbolList[i].Name;
                }
                else
                {
                    radioButtons[i].Text = familySymbolList[i].FamilyName + "_" + familySymbolList[i].Name;
                }
                radioButtons[i].AutoSize = true;
                radioButtons[i].Location = new System.Drawing.Point(5, 5 + i * 25);
                radioBtnPanel.Controls.Add(radioButtons[i]);
                if (i == 0)
                {
                    radioButtons[0].Checked = true; // 預設第一個
                }
            }
        }
        // 新增CheckBox, Excel中全部的Sheet
        private void CreateCheckBox()
        {
            CheckBox[] checkBoxs = new CheckBox[] { };
            List<string> sheetNames = Sinotech_API.sheetNames;
            checkBoxs = new CheckBox[sheetNames.Count];
            for (int i = 0; i < sheetNames.Count; i++)
            {
                checkBoxs[i] = new CheckBox();
                checkBoxs[i].Font = new Font("微軟正黑體", 10, FontStyle.Regular);
                checkBoxs[i].Text = sheetNames[i];
                checkBoxs[i].AutoSize = true;
                checkBoxs[i].Location = new System.Drawing.Point(5, 5 + i * 25);
                sheetPanel.Controls.Add(checkBoxs[i]);
                checkBoxs[i].Checked = true; // 預設全選
            }
        }
        // 確定
        private void SureBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = true; // 確定執行
            // 選擇的圖框名稱
            foreach (System.Windows.Forms.Control rbControl in radioBtnPanel.Controls)
            {
                RadioButton radioBtn = rbControl as RadioButton;
                if (radioBtn != null && radioBtn.Checked)
                {
                    this.titleBlocksName = radioBtn.Text;
                    break;
                }
            }
            // 選擇的Sheet名稱
            foreach (System.Windows.Forms.Control spControl in sheetPanel.Controls)
            {
                CheckBox checkBox = spControl as CheckBox;
                if (checkBox != null && checkBox.Checked)
                {
                    this.checkSheets.Add(checkBox.Text);
                }
            }
            Close();
        }
        // 取消
        private void CancelBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = false; // 取消執行
            Close();
        }
    }
}
