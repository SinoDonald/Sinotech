using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Sinotech_2025;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Sinotech_2025
{
    public partial class ProfessionalCodeForm : System.Windows.Forms.Form
    {
        public string filePath = string.Empty; // 專業代碼路徑
        public int prjCount = 0; // 專案名稱解析"-"
        public int prjCode = 1; // 專案代碼
        public List<ProfessionalCode> professionalCodeList = new List<ProfessionalCode>();
        public List<ProfessionalCode> combinePCodes = new List<ProfessionalCode>(); // 整合重複的專業代碼
        public class ProfessionalCode
        {
            public List<string> comments = new List<string>();
            public string professionalCode { get; set;}
        }
        public bool trueOrFalse = false;
        public ProfessionalCodeForm(List<RevitLinkInstance> rvtLinkInsList, int prjCount)
        {
            InitializeComponent();
            this.prjCode = 1; // 專案代碼
            App sinotech_Button = new App();
            this.filePath = Path.Combine(Directory.GetParent(sinotech_Button.addinAssmeblyPath).FullName, "專業代碼.txt");
            this.prjCount = prjCount;
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
            for(int i = 0; i < prjCount; i++)
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
                // 寫入文字檔
                List<string> professionalCodes = LoadProfessionalCode(); // 載入專業代碼
                if(textBox1.Text != "") { professionalCodes.Add(textBox1.Text); }
                foreach (string professionalCode in professionalCodes.Distinct().OrderBy(x => x).ToList())
                {
                    content += professionalCode + "\n";
                }
                if(content.Length > 0)
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
                if(comboBox1.Text.Equals("") && textBox1.Text.Equals(""))
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
                            string comment = projectName.Split('-')[prjCode];
                            professionalCode.comments.Add(comment);
                            removeProjectNames.Add(projectName);
                        }
                        catch(Exception ex) { string error = ex.Message + "\n" + ex.ToString(); }
                    }
                    foreach(string removeProjectName in removeProjectNames)
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
    }
}
