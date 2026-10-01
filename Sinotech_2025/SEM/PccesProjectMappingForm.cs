using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sinotech_2025.SEM
{
    internal sealed class PccesProjectMappingForm : Form
    {
        private readonly DataGridView grid;
        private readonly PccesProjectMappingStore store;
        private readonly Dictionary<string, string> savedMappings;

        private void InitializeComponent()
        {

        }

        public Dictionary<string, string> ProjectMapping { get; private set; }
                    = new Dictionary<string, string>(StringComparer.Ordinal);

        public PccesProjectMappingForm(List<string> codes, List<OutPutPCCES.ItemDescription> descriptions,
            PccesProjectMappingStore store = null)
        {
            this.store = store ?? new PccesProjectMappingStore();
            string settingsWarning = string.Empty;
            try { savedMappings = this.store.Load(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
            {
                savedMappings = new Dictionary<string, string>(StringComparer.Ordinal);
                settingsWarning = "\n無法讀取上次設定，本次使用預設分類。";
            }
            Text = "PCCES 備註代碼分類";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 500);
            MinimumSize = new Size(420, 380);
            Font = new Font("Microsoft JhengHei", 10);
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var instructions = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 0, 0, 10),
                Text = "備註取底線前的代碼，例如 DR_… → DR。\n沿用上次選擇；未記錄的代碼套用預設分類。\n淡黃色「新增」表示首次出現，請確認項次。\n空白備註歸入 A3；按確認後記住本次選擇。\n" +
                    string.Join("\n", new[] { "A1", "A2", "A3" }.Select(item => item + "：" +
                        (descriptions?.FirstOrDefault(x => x.item == item)?.description ?? ""))) + settingsWarning
            };
            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Code",
                HeaderText = "備註代碼",
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            var projectColumn = new DataGridViewComboBoxColumn
            {
                Name = "Project",
                HeaderText = "對應項次",
                Width = 95,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            projectColumn.Items.AddRange("A1", "A2", "A3");
            grid.Columns.Add(projectColumn);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Status",
                HeaderText = "狀態",
                ReadOnly = true,
                Width = 80,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            foreach (string code in codes.Distinct().OrderBy(x => x))
            {
                bool recorded = savedMappings.TryGetValue(code, out string project);
                int index = grid.Rows.Add(code, recorded ? project : PccesProjectMappingStore.GetDefaultProject(code),
                    recorded ? "已記錄" : "新增");
                if (!recorded)
                {
                    grid.Rows[index].DefaultCellStyle.BackColor = Color.LightGoldenrodYellow;
                    grid.Rows[index].DefaultCellStyle.ForeColor = Color.FromArgb(90, 60, 0);
                    grid.Rows[index].DefaultCellStyle.SelectionBackColor = Color.Goldenrod;
                    grid.Rows[index].DefaultCellStyle.SelectionForeColor = Color.Black;
                }
            }
            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 10, 0, 0)
            };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            var confirm = new Button { Text = "確認並匯出", AutoSize = true };
            confirm.Click += (sender, args) =>
            {
                grid.EndEdit();
                var selectedMappings = grid.Rows.Cast<DataGridViewRow>().ToDictionary(
                    row => (string)row.Cells["Code"].Value,
                    row => row.Cells["Project"].Value as string ?? "A3", StringComparer.Ordinal);
                // 保留這次模型未出現的代碼，切換模型後仍可沿用。
                var mergedMappings = new Dictionary<string, string>(savedMappings, StringComparer.Ordinal);
                foreach (var mapping in selectedMappings) mergedMappings[mapping.Key] = mapping.Value;
                try { this.store.Save(mergedMappings); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                {
                    MessageBox.Show(this, "無法儲存 PCCES 分類設定，請確認資料夾權限後重試。\n" + ex.Message,
                        "PCCES 分類設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ProjectMapping = selectedMappings;
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(confirm);
            layout.Controls.Add(instructions, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            AcceptButton = confirm;
            CancelButton = cancel;
        }
    }
}
