using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;

namespace Sinotech_2020.UpdateView
{
    public partial class EditViewSheetNumberForm : System.Windows.Forms.Form
    {
        Document revitDoc = null;

        public EditViewSheetNumberForm(Document doc)
        {
            revitDoc = doc;
            InitializeComponent();
            CreateDGVData(); // 新增DataGridView資料
            CenterToParent(); // 置中
        }
        // 新增DataGridView資料
        private void CreateDGVData()
        {
            dataGridView1.Dock = DockStyle.Fill; // 填滿視窗
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("原圖號", typeof(string));
            dataTable.Columns.Add("更新圖號", typeof(string));
            dataGridView1.DataSource = dataTable;
            dataGridView1.Columns[0].ReadOnly = true; // 原圖號唯讀
            dataGridView1.EditMode = DataGridViewEditMode.EditOnEnter; // 避免連續輸入文字
            List<ViewSheet> viewSheets = new FilteredElementCollector(revitDoc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList();
            foreach (ViewSheet viewSheet in viewSheets)
            {
                DataRow row = dataTable.NewRow();
                Parameter para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                string number = para.AsString();
                row[0] = number;                
                row[1] = "";
                dataTable.Rows.Add(row);
            }
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            List<KeyValuePair<string, string>> keyValueList = new List<KeyValuePair<string, string>>();
            // 儲存所有DataGridView資料
            foreach(DataGridViewRow row in dataGridView1.Rows)
            {
                if(row.Cells[0].Value != null)
                {
                    string value = row.Cells[1].Value.ToString();
                    if (!String.IsNullOrEmpty(value))
                    {
                        KeyValuePair<string, string> keyValuePair = new KeyValuePair<string, string>(row.Cells[0].Value.ToString(), row.Cells[1].Value.ToString());
                        keyValueList.Add(keyValuePair);
                    }
                }
            }            
            var duplicate = keyValueList.GroupBy(i => i.Value).Where(g => g.Count() > 1).Select(g => g.ElementAt(0)); // 檢查圖號是否有重複值
            if (duplicate.Count() > 0)
            {
                TaskDialog.Show("Error", "圖紙號碼重複, 無法進行變更。");
            }
            else
            {
                List<KeyValuePair<string, string>> newKeyValueList = new List<KeyValuePair<string, string>>();
                List<ViewSheet> viewSheets = new FilteredElementCollector(revitDoc).OfClass(typeof(ViewSheet)).WhereElementIsNotElementType().Cast<ViewSheet>().ToList();
                // 先將要修改的圖號設定為自己的ID
                using (Transaction trans = new Transaction(revitDoc, "更新圖號"))
                {
                    trans.Start();
                    foreach (KeyValuePair<string, string> keyValuePair in keyValueList)
                    {
                        ViewSheet viewSheet = viewSheets.Where(x => x.SheetNumber.Equals(keyValuePair.Key)).FirstOrDefault();
                        if (viewSheet != null)
                        {
                            try
                            {
                                Parameter para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                para.Set(viewSheet.Id.ToString());
                                KeyValuePair<string, string> keyValue = new KeyValuePair<string, string>(viewSheet.Id.ToString(), keyValuePair.Value);
                                newKeyValueList.Add(keyValue);
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    trans.Commit();
                }
                // 再依照自己的ID調整成要更換的圖紙號碼
                using (Transaction trans = new Transaction(revitDoc, "更新圖號"))
                {
                    trans.Start();
                    foreach (KeyValuePair<string, string> keyValue in newKeyValueList)
                    {
                        ViewSheet viewSheet = viewSheets.Where(x => x.SheetNumber.Equals(keyValue.Key)).FirstOrDefault();
                        if (viewSheet != null)
                        {
                            try
                            {
                                Parameter para = viewSheet.get_Parameter(BuiltInParameter.SHEET_NUMBER);
                                para.Set(keyValue.Value);
                            }
                            catch (Exception)
                            {

                            }
                        }
                    }
                    trans.Commit();
                }

                Close();
            }
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
