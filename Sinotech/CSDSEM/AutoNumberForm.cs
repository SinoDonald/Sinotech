using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;

namespace Sinotech.CSDSEM
{
    public partial class AutoNumberForm : System.Windows.Forms.Form
    {
        public bool trueOrFalse = false;
        public string viewFamilyTypeName = string.Empty; // 選取ViewFamilyType的名稱

        // 【新增】用來記錄是否成功找到符合條件的視圖，若為 false 則阻止表單開啟
        private bool HasValidViews = false;

        public AutoNumberForm(Document doc)
        {
            InitializeComponent();

            // 將 CreateRadioButton 改為回傳 bool，判斷是否成功建立選項
            HasValidViews = CreateRadioButton(doc, radioBtnPanel);

            CenterToParent();
        }
        // 新增RadioButton, 從專案中找到全部的圖框
        public bool CreateRadioButton(Document doc, System.Windows.Forms.Panel radioBtnPanel)
        {
            radioBtnPanel.Controls.Clear(); // 清空radiobutton

            // =========================================================
            // 【第一道防線】：嚴格篩選出真正符合所有後續條件的視圖
            // 條件：有 GenLevel + 圖面分類為「出圖」 + 沒有子視圖 (Count == 0)
            // =========================================================
            List<ViewPlan> validViewPlans = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .WhereElementIsNotElementType()
                .Cast<ViewPlan>()
                .Where(v => v.GenLevel != null)
                .Where(v => v.LookupParameter("圖面分類") != null && v.LookupParameter("圖面分類").AsString() == "出圖")
                .Where(v => v.GetDependentViewIds().Count == 0) // 排除有子視圖的母視圖
                .ToList();

            // 防呆 1：如果專案中連一個完全符合條件的視圖都沒有
            if (validViewPlans.Count == 0)
            {
                TaskDialog.Show("提示", "專案中找不到可供標籤的平面圖！\n\n請確認：\n1. 視圖的「圖面分類」參數是否為「出圖」。\n2. 該視圖不能是含有子視圖的母視圖。");
                return false;
            }

            // 取得這些有效視圖所屬的類型 ID
            HashSet<ElementId> validTypeIds = new HashSet<ElementId>(validViewPlans.Select(v => v.GetTypeId()));

            // =========================================================
            // 篩選 ViewFamilyType (只保留底下確實有合法視圖的類型)
            // =========================================================
            List<ViewFamilyType> viewFamilyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(x => x.ViewFamily == ViewFamily.FloorPlan || x.ViewFamily == ViewFamily.CeilingPlan)
                .Where(x => x.Name.Contains("1/100"))
                .Where(x => validTypeIds.Contains(x.Id)) // 【關鍵過濾】
                .OrderBy(x => x.ViewFamily)
                .ThenBy(x => x.Name)
                .ToList();

            if (viewFamilyTypes.Count == 0)
            {
                TaskDialog.Show("提示", "專案中沒有找到符合條件的視圖類型！");
                return false;
            }

            // 產生 RadioButton 選項
            RadioButton[] radioButtons = new RadioButton[viewFamilyTypes.Count];
            for (int i = 0; i < viewFamilyTypes.Count; i++)
            {
                radioButtons[i] = new RadioButton();
                radioButtons[i].Font = new Font("微軟正黑體", 10, FontStyle.Regular);
                if (viewFamilyTypes[i].FamilyName.Equals(viewFamilyTypes[i].Name))
                {
                    radioButtons[i].Text = viewFamilyTypes[i].Name;
                }
                else
                {
                    radioButtons[i].Text = viewFamilyTypes[i].FamilyName + " (" + viewFamilyTypes[i].Name + ")";
                }
                radioButtons[i].AutoSize = true;
                radioButtons[i].Location = new System.Drawing.Point(5, 5 + i * 25);
                radioBtnPanel.Controls.Add(radioButtons[i]);

                if (i == 0) { radioButtons[0].Checked = true; } // 預設第一個
            }

            return true; // 成功建立選項
        }

        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = true;
            // 選取ViewFamilyType的名稱
            foreach (System.Windows.Forms.Control rbControl in radioBtnPanel.Controls)
            {
                RadioButton radioBtn = rbControl as RadioButton;
                if (radioBtn != null && radioBtn.Checked) { this.viewFamilyTypeName = radioBtn.Text; break; }
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        // =========================================================
        // 【新增機制】在表單即將顯示前，如果發現沒有合法視圖，直接關閉表單
        // =========================================================
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!HasValidViews)
            {
                this.DialogResult = DialogResult.Cancel;
                this.Close(); // 安全阻斷，表單不會閃現
            }
        }
    }
}