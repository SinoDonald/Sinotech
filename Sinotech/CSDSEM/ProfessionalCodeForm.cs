using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TaskDialog = Autodesk.Revit.UI.TaskDialog;
using TextBox = System.Windows.Forms.TextBox;

namespace Sinotech.CSDSEM
{
    public partial class ProfessionalCodeForm : System.Windows.Forms.Form
    {
        public string filePath = string.Empty; // 專業代碼路徑
        public int prjCount = 0; // 專案名稱解析"-"
        public int prjCode = 1; // 專案代碼
        public double elevationOffset = 0.0; // 高程偏移
        public List<PrjNameAndCode> prjNameAndCodes = new List<PrjNameAndCode>();
        public List<ProfessionalCode> professionalCodeList = new List<ProfessionalCode>();
        public List<ProfessionalCode> combinePCodes = new List<ProfessionalCode>(); // 整合重複的專業代碼
        public class PrjNameAndCode
        {
            public string projectName { get; set; }
            public string professionalCode { get; set; }
        }
        public class ProfessionalCode
        {
            public List<string> comments = new List<string>();
            public string professionalCode { get; set; }
        }
        public bool trueOrFalse = false;
        public ProfessionalCodeForm(List<RevitLinkInstance> rvtLinkInsList, int prjCount)
        {
            InitializeComponent();
            this.prjCode = 1; // 專案代碼
            App sinotech_Button = new App();
            try
            {
                this.filePath = Path.Combine(Directory.GetParent(sinotech_Button.addinAssmeblyPath).FullName, "專業代碼.txt");
            }
            catch
            {
                this.filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "專業代碼.txt");
                // 檢查檔案是否存在
                if (!File.Exists(this.filePath))
                {
                    // 建立檔案並立即關閉 FileStream，避免檔案被程式鎖定
                    using (File.Create(this.filePath)) { }
                }
            }
            this.prjCount = prjCount;
            prjNameAndCodes = new List<PrjNameAndCode>();
            LoadProfessionalCode(); // 載入專業代碼
            CreateNodes(rvtLinkInsList); // 新增節點
            CenterToParent();
        }
        // 新增節點
        private void CreateNodes(List<RevitLinkInstance> rvtLinkInsList)
        {
            checkedListBox1.Items.Clear(); // 清空節點
            try
            {
                List<string> hostNames = rvtLinkInsList.Select(x => x.Name.Trim().Split(':')[0]).Distinct().OrderBy(x => x).ToList();
                foreach (string hostName in hostNames)
                {
                    checkedListBox1.Items.Add(hostName);
                }
            }
            catch (Exception) { }
        }
        // 載入專業代碼
        private List<string> LoadProfessionalCode()
        {
            comboBox1.Items.Clear(); // 清空
            comboBox1.Items.Add("");
            List<string> professionalCodes = new List<string>();
            try
            {
                // 先檢查是否有此檔案, 沒有的話則新增
                string folderPath = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
                if (!File.Exists(filePath))
                {
                    using (FileStream fs = File.Create(filePath))
                    {

                    }
                }
                using (StreamReader sr = new StreamReader(filePath))
                {
                    string line = sr.ReadLine();
                    while (line != null)
                    {
                        if (line != "")
                        {
                            professionalCodes.Add(line);
                            comboBox1.Items.Add(line);
                        }
                        line = sr.ReadLine();
                    }
                    sr.Close();
                }
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

            comboBox2.Items.Clear(); // 清空
            for (int i = 0; i < prjCount; i++)
            {
                comboBox2.Items.Add(i);
            }
            return professionalCodes;
        }
        // 專業代碼
        private void prjCodeBtn_Click(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(comboBox2.Text)) { comboBox2.Text = "1"; }
            prjCode = Convert.ToInt32(comboBox2.Text);
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            string content = string.Empty;
            try
            {
                elevationOffset = Convert.ToDouble(elevOffsetTB.Text); // 高程偏移
                // 寫入文字檔
                List<string> professionalCodes = LoadProfessionalCode(); // 載入專業代碼
                if (textBox1.Text != "") { professionalCodes.Add(textBox1.Text); }
                foreach (string professionalCode in professionalCodes.Distinct().OrderBy(x => x).ToList())
                {
                    content += professionalCode + "\n";
                }
                if (content.Length > 0)
                {
                    content = content.Substring(0, content.Length - 1);
                }
                using (StreamWriter sw = new StreamWriter(filePath))
                {
                    sw.WriteLine(content);
                    sw.Close();
                }
                LoadProfessionalCode(); // 載入專業代碼
            }
            catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }

            // 選擇連結專案
            if (checkedListBox1.CheckedItems.Count > 0)
            {
                ProfessionalCode professionalCode = new ProfessionalCode();
                if (comboBox1.Text.Equals("") && textBox1.Text.Equals(""))
                {
                    TaskDialog.Show("Revit", "請輸入要替換的專業代碼");
                }
                else
                {
                    if (comboBox1.Text != "")
                    {
                        professionalCode.professionalCode = comboBox1.Text;
                    }
                    else
                    {
                        professionalCode.professionalCode = textBox1.Text;
                    }
                    List<string> removeProjectNames = new List<string>();
                    foreach (string projectName in checkedListBox1.CheckedItems)
                    {
                        try
                        {
                            string projectNameWithoutExtension = Path.GetFileNameWithoutExtension(projectName); // 移除副檔名
                            string comment = ParseFileNameByString(projectNameWithoutExtension)[prjCode];
                            professionalCode.comments.Add(comment);
                            removeProjectNames.Add(projectName);

                            // 連結專案與選擇替換Index後的名稱
                            PrjNameAndCode prjNameAndCode = new PrjNameAndCode();
                            prjNameAndCode.projectName = projectName;
                            prjNameAndCode.professionalCode = comment;
                            prjNameAndCodes.Add(prjNameAndCode);
                        }
                        catch (Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }
                    foreach (string removeProjectName in removeProjectNames)
                    {
                        checkedListBox1.Items.Remove(removeProjectName);
                    }
                }
                professionalCodeList.Add(professionalCode);
            }
            else
            {
                TaskDialog.Show("Revit", "請先選擇連結專案");
            }
        }

        /// <summary>
        /// 【強健檔名剖析工具】：清除檔案系統非法字元並透過正則表達式拆分多重分隔符
        /// </summary>
        /// <param name="doc">Revit Document</param>
        /// <returns>檔名簡碼 Token 清單</returns>
        private static List<string> ParseFileNameByString(string rawFileName)
        {
            if (string.IsNullOrWhiteSpace(rawFileName)) return new List<string>();

            // 2. 移除作業系統非法的檔案字元
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char invalidChar in invalidChars)
            {
                rawFileName = rawFileName.Replace(invalidChar, ' ');
            }

            // 3. 正則拆分常見分隔符：-, _, ., #, 空白
            string[] tokens = Regex.Split(rawFileName.Trim(), @"[\-_.\s#]+");

            return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }
        // 清除專業代碼
        private void deleteBtn_Click(object sender, EventArgs e)
        {
            File.Delete(filePath); // 刪除檔案
            LoadProfessionalCode(); // 載入專業代碼
        }
        // 完成
        private void finishBtn_Click(object sender, EventArgs e)
        {
            // 整合重複的
            List<string> pCodes = professionalCodeList.Select(x => x.professionalCode).Distinct().ToList();
            foreach (string pCode in pCodes)
            {
                ProfessionalCode combinePCode = new ProfessionalCode();
                combinePCode.professionalCode = pCode;
                List<ProfessionalCode> sameProfessionalCodes = professionalCodeList.Where(x => x.professionalCode.Equals(pCode)).ToList();
                foreach (ProfessionalCode sameProfessionalCode in sameProfessionalCodes)
                {
                    foreach (string comments in sameProfessionalCode.comments)
                    {
                        combinePCode.comments.Add(comments);
                    }
                }
                combinePCodes.Add(combinePCode);
            }
            trueOrFalse = true;
            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = false;
            Close();
        }
        // 限制TextBox 只能輸入數字，以及限制不能使用快速鍵
        private void elevOffsetTB_KeyPress(object sender, KeyPressEventArgs e)
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
        private void elevOffsetTB_TextChanged(object sender, EventArgs e)
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
