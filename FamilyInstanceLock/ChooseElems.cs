using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace FamilyInstanceLock
{
    public partial class ChooseElems : System.Windows.Forms.Form
    {
        UIApplication revitUIApp = null;
        UIDocument revitUIDoc = null;
        Document revitDoc = null;
        private RevitDocument m_connect = null;
        ExternalEvent m_externalEvent_FamilyConversionToDirectShape;
        public class FamilyAndSymbol
        {
            public string familyName { get; set; }
            public string symbolName { get; set; }
        }
        public List<FamilyInstance> familyInstances = new List<FamilyInstance>();
        public static List<FamilyInstance> chooseFamilys = new List<FamilyInstance>();
        public List<FamilyAndSymbol> familyAndSymbolNames = new List<FamilyAndSymbol>();
        List<string> checkedNodesList = new List<string>(); // 儲存選取的節點
        public bool trueOrFalse = true;

        /// <summary>
        /// 自訂ListView滾輪只有上下滑動
        /// </summary>
        public class NativeMethods
        {
            public const int GWL_STYLE = -16;
            public const int WS_HSCROLL = 0x00100000;
            [DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        }
        private void HideHorizontalScrollBar(ListView listView)
        {
            int style = NativeMethods.GetWindowLong(listView.Handle, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(listView.Handle, NativeMethods.GWL_STYLE, style & ~NativeMethods.WS_HSCROLL);
        }
        public ChooseElems(UIApplication uiapp, RevitDocument connect, ExternalEvent externalEvent_FamilyConversionToDirectShape)
        {
            revitUIApp = uiapp;
            revitUIDoc = connect.RevitDoc;
            revitDoc = connect.RevitDoc.Document;
            m_connect = connect;
            m_externalEvent_FamilyConversionToDirectShape = externalEvent_FamilyConversionToDirectShape;
            this.familyInstances = new List<FamilyInstance>();

            InitializeComponent();
            CreateRadioButton(revitDoc);
            CenterToParent();

            familyLV.View = System.Windows.Forms.View.Details;
            foreach (ColumnHeader column in familyLV.Columns) { column.Width = familyLV.ClientSize.Width / familyLV.Columns.Count; }
            HideHorizontalScrollBar(familyLV); // 自訂ListView滾輪只有上下滑動
        }
        // 新增RadioButton, 從專案中找到族群
        private void CreateRadioButton(Document doc)
        {
            // 搜尋所有專案中的FamilySymbol
            try
            {
                ElementCategoryFilter genericModels = new ElementCategoryFilter(BuiltInCategory.OST_GenericModel); // 一般模型
                ElementCategoryFilter plumbingFixtures = new ElementCategoryFilter(BuiltInCategory.OST_PlumbingFixtures); // 衛工裝置
                ElementCategoryFilter furnitures = new ElementCategoryFilter(BuiltInCategory.OST_Furniture); // 傢俱
                ElementCategoryFilter sites = new ElementCategoryFilter(BuiltInCategory.OST_Site); // 敷地
                ElementCategoryFilter mechanicalEquipments = new ElementCategoryFilter(BuiltInCategory.OST_MechanicalEquipment); // 機械設備
                ElementCategoryFilter specialityEquipments = new ElementCategoryFilter(BuiltInCategory.OST_SpecialityEquipment); // 特製設備                
                List<ElementFilter> filters = new List<ElementFilter>() { genericModels, plumbingFixtures, furnitures, sites, mechanicalEquipments, specialityEquipments };
                LogicalOrFilter familyInsFilter = new LogicalOrFilter(filters);
                familyInstances = new FilteredElementCollector(doc).WherePasses(familyInsFilter).WhereElementIsNotElementType()
                                 .OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().OrderBy(x => x.Symbol.Family.Name).ToList();
                List<string> familyNames = familyInstances.Select(x => x.Symbol.Family.Name).Distinct().ToList();
                foreach (string familyName in familyNames)
                {
                    List<string> symbolNames = familyInstances.Where(x => x.Symbol.Family.Name.Equals(familyName)).Select(x => x.Symbol.Name).Distinct().ToList();
                    foreach (string symbolName in symbolNames)
                    {
                        FamilyAndSymbol familyAndSymbol = new FamilyAndSymbol();
                        familyAndSymbol.familyName = familyName;
                        familyAndSymbol.symbolName = symbolName;
                        familyAndSymbolNames.Add(familyAndSymbol);
                    }
                }                
            }
            catch (Exception ex) { TaskDialog.Show("獲取同族群元件失敗", ex.Message); }
            familyLV.Items.Clear();
            foreach (FamilyAndSymbol familyAndSymbolName in familyAndSymbolNames) 
            { familyLV.Items.Add(familyAndSymbolName.familyName/* + "：" + familyAndSymbolName.symbolName*/); }
            // 預設全選
            for (int i = 0; i < familyLV.Items.Count; i++)
            {
                familyLV.Items[i].Checked = true;
            }
            familyLV.View = System.Windows.Forms.View.List;
        }
        // 確定
        private void sureBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = true;
            chooseFamilys = new List<FamilyInstance>();

            foreach (ListViewItem item in familyLV.Items)
            {
                if (item.Checked == true)
                {
                    List<FamilyInstance> familys = familyInstances.Where(x => x.Symbol.Family.Name.Equals(item.Text)).Where(x => x.DesignOption == null).ToList();
                    foreach (FamilyInstance family in familys) { chooseFamilys.Add(family); }
                    familys = familyInstances.Where(x => x.Symbol.Family.Name.Equals(item.Text)).Where(x => x.DesignOption != null).Where(x => x.DesignOption.IsPrimary.Equals(true)).ToList();
                    foreach (FamilyInstance family in familys) { chooseFamilys.Add(family); }
                }
            }
            m_externalEvent_FamilyConversionToDirectShape.Raise();
            Close();
        }
        // 取消
        private void cancelBtn_Click(object sender, EventArgs e)
        {
            trueOrFalse = false;
            Close();
        }
        // 全選
        private void allRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < familyLV.Items.Count; i++)
            {
                familyLV.Items[i].Checked = true;
            }
        }
        // 全部取消
        private void allCancelRbtn_CheckedChanged(object sender, EventArgs e)
        {
            for (int i = 0; i < familyLV.Items.Count; i++)
            {
                familyLV.Items[i].Checked = false;
            }
        }
        // 點選ListView item文字即可勾選或取消
        private void familyLV_SelectedIndexChanged(object sender, EventArgs e)
        {
            ListView selectListView = sender as ListView;
            ListViewItem focusedItem = selectListView.FocusedItem;
            if (selectListView.SelectedItems.Count > 0)
            {
                if (focusedItem.Checked == true)
                {
                    focusedItem.Checked = false;
                }
                else
                {
                    focusedItem.Checked = true;
                }
            }
        }
    }
}
