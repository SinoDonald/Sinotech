using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Form = System.Windows.Forms.Form;

namespace Sinotech_2025.SpeedTool
{
    public partial class PipeEditForm : Form
    {
        //外部事件處理:讀取其他的cs檔
        ExternalEvent m_externalEvent_PipeCodeColor; // 管線代碼與色彩修改
        public static string code = string.Empty; // 原本名稱
        public static string newName = string.Empty; // 新名稱
        public static string color = string.Empty; // 顏色

        public PipeEditForm(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            InitializeComponent();
            CenterToParent();

            IExternalEventHandler handler_PipeCodeColor = new PipeCodeColor(); // 管線代碼與色彩修改
            ExternalEvent externalEvent_PipeCodeColor = ExternalEvent.Create(handler_PipeCodeColor);
            m_externalEvent_PipeCodeColor = externalEvent_PipeCodeColor;

            List<Element> pipingSystemTypes = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).ToElements().ToList();
            List<string> pipingSystemTypeNames = pipingSystemTypes.Select(x => x.Name).Distinct().OrderBy(x => x).ToList();
            comboBox1.Items.Clear();
            foreach (string pipingSystemTypeName in pipingSystemTypeNames)
            {
                comboBox1.Items.Add(pipingSystemTypeName);
            }
            comboBox1.Text = comboBox1.Items[0].ToString(); // 預設選擇第一個
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            code = comboBox1.Text;
            newName = newNameTB.Text;
            color = newColorTB.Text;
            m_externalEvent_PipeCodeColor.Raise(); // 呼叫外部事件處理:讀取其他的cs檔
            //Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
