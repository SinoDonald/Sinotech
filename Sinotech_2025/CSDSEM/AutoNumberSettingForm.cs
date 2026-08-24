using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Control = System.Windows.Forms.Control;
using Point = System.Drawing.Point;

namespace Sinotech_2025.CSDSEM
{
    public class AutoNumberSettingForm : System.Windows.Forms.Form
    {
        private DataGridView dgvLinks;
        private ListBox lbCasingOrder;
        private ListBox lbOpeningOrder;
        private Button btnCasingUp, btnCasingDown;
        private Button btnOpeningUp, btnOpeningDown;
        private Button btnOk, btnCancel;

        public List<LinkConfigItem> ConfigItems { get; private set; } = new List<LinkConfigItem>();
        public NumberingExecutionSettings ResultSettings { get; private set; }

        public AutoNumberSettingForm(List<RevitLinkInstance> linkInstances)
        {
            InitializeComponent();
            LoadData(linkInstances);
        }

        private void InitializeComponent()
        {
            this.Text = "開口與套管自動編號設定";
            this.Size = new Size(950, 550);
            this.MinimumSize = new Size(850, 450);
            this.Font = new Font("微軟正黑體", 9.5F, FontStyle.Regular);
            this.StartPosition = FormStartPosition.CenterScreen;

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(10)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 88F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 12F));

            // 左側：DataGridView
            dgvLinks = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            DataGridViewTextBoxColumn colName = new DataGridViewTextBoxColumn { Name = "FileName", HeaderText = "連結模型檔名", Width = 230, ReadOnly = true };
            DataGridViewComboBoxColumn colTokenIndex = new DataGridViewComboBoxColumn { Name = "TokenIndex", HeaderText = "欄位解析", Width = 90 };
            DataGridViewTextBoxColumn colCode = new DataGridViewTextBoxColumn { Name = "Code", HeaderText = "專業代碼", Width = 80, ReadOnly = true };
            DataGridViewCheckBoxColumn colCasing = new DataGridViewCheckBoxColumn { Name = "IsCasing", HeaderText = "套管", Width = 55 };
            DataGridViewCheckBoxColumn colOpening = new DataGridViewCheckBoxColumn { Name = "IsOpening", HeaderText = "開口", Width = 55 };

            dgvLinks.Columns.AddRange(colName, colTokenIndex, colCode, colCasing, colOpening);
            dgvLinks.CellValueChanged += DgvLinks_CellValueChanged;
            dgvLinks.CurrentCellDirtyStateChanged += (s, e) => { if (dgvLinks.IsCurrentCellDirty) dgvLinks.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            mainLayout.Controls.Add(dgvLinks, 0, 0);

            // 右側：排序清單控制項
            TableLayoutPanel rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            // 套管排序 Group
            GroupBox gbCasing = new GroupBox { Text = "套管編號順序 (由上至下)", Dock = DockStyle.Fill };
            lbCasingOrder = new ListBox { Dock = DockStyle.Left, Width = 180 };
            btnCasingUp = new Button { Text = "▲ 上移", Size = new Size(70, 30), Location = new Point(190, 25) };
            btnCasingDown = new Button { Text = "▼ 下移", Size = new Size(70, 30), Location = new Point(190, 65) };
            btnCasingUp.Click += (s, e) => MoveItem(lbCasingOrder, -1);
            btnCasingDown.Click += (s, e) => MoveItem(lbCasingOrder, 1);
            gbCasing.Controls.AddRange(new Control[] { lbCasingOrder, btnCasingUp, btnCasingDown });

            // 開口排序 Group
            GroupBox gbOpening = new GroupBox { Text = "開口編號順序 (由上至下)", Dock = DockStyle.Fill };
            lbOpeningOrder = new ListBox { Dock = DockStyle.Left, Width = 180 };
            btnOpeningUp = new Button { Text = "▲ 上移", Size = new Size(70, 30), Location = new Point(190, 25) };
            btnOpeningDown = new Button { Text = "▼ 下移", Size = new Size(70, 30), Location = new Point(190, 65) };
            btnOpeningUp.Click += (s, e) => MoveItem(lbOpeningOrder, -1);
            btnOpeningDown.Click += (s, e) => MoveItem(lbOpeningOrder, 1);
            gbOpening.Controls.AddRange(new Control[] { lbOpeningOrder, btnOpeningUp, btnOpeningDown });

            rightPanel.Controls.Add(gbCasing, 0, 0);
            rightPanel.Controls.Add(gbOpening, 0, 1);
            mainLayout.Controls.Add(rightPanel, 1, 0);

            // 底部按鈕
            FlowLayoutPanel bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            btnCancel = new Button { Text = "取消", Size = new Size(90, 32), DialogResult = DialogResult.Cancel };
            btnOk = new Button { Text = "開始編號", Size = new Size(100, 32), DialogResult = DialogResult.OK };
            btnOk.Click += BtnOk_Click;
            bottomPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
            mainLayout.Controls.Add(bottomPanel, 1, 1);

            this.Controls.Add(mainLayout);
        }

        private void LoadData(List<RevitLinkInstance> linkInstances)
        {
            foreach (var link in linkInstances)
            {
                Document linkDoc = link.GetLinkDocument();
                string rawName = linkDoc != null && !string.IsNullOrEmpty(linkDoc.PathName)
                    ? Path.GetFileNameWithoutExtension(linkDoc.PathName)
                    : link.Name.Split(':')[0].Trim();

                string cleanName = Path.GetFileNameWithoutExtension(rawName);
                List<string> tokens = ParseTokens(cleanName);

                int defaultIndex = tokens.Count > 2 ? 2 : (tokens.Count > 1 ? 1 : 0);
                string code = tokens.Count > defaultIndex ? tokens[defaultIndex] : "";

                // 預設規則判定
                bool isCasingDefault = new[] { "AP", "WS", "DS", "FP" }.Contains(code);
                bool isOpeningDefault = new[] { "AD", "EP", "EE", "ELE", "AFC", "COM", "PSD", "PSY" }.Contains(code);

                var item = new LinkConfigItem
                {
                    LinkInstance = link,
                    CleanFileName = cleanName,
                    Tokens = tokens,
                    SelectedTokenIndex = defaultIndex,
                    IsCasing = isCasingDefault,
                    IsOpening = isOpeningDefault
                };
                ConfigItems.Add(item);
            }

            BindGrid();
            RefreshOrderLists();
        }

        private void BindGrid()
        {
            dgvLinks.Rows.Clear();
            foreach (var item in ConfigItems)
            {
                int rowIndex = dgvLinks.Rows.Add();
                DataGridViewRow row = dgvLinks.Rows[rowIndex];
                row.Tag = item;
                row.Cells["FileName"].Value = item.CleanFileName;

                var comboCell = (DataGridViewComboBoxCell)row.Cells["TokenIndex"];
                comboCell.Items.Clear();
                for (int i = 0; i < item.Tokens.Count; i++)
                {
                    comboCell.Items.Add($"欄位 {i} ({item.Tokens[i]})");
                }
                if (item.SelectedTokenIndex < item.Tokens.Count)
                {
                    comboCell.Value = comboCell.Items[item.SelectedTokenIndex];
                }

                row.Cells["Code"].Value = item.ExtractedCode;
                row.Cells["IsCasing"].Value = item.IsCasing;
                row.Cells["IsOpening"].Value = item.IsOpening;
            }
        }

        private void DgvLinks_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            DataGridViewRow row = dgvLinks.Rows[e.RowIndex];
            var item = row.Tag as LinkConfigItem;
            if (item == null) return;

            if (e.ColumnIndex == dgvLinks.Columns["TokenIndex"].Index)
            {
                var comboCell = (DataGridViewComboBoxCell)row.Cells["TokenIndex"];
                int newIndex = comboCell.Items.IndexOf(comboCell.Value);
                if (newIndex >= 0)
                {
                    item.SelectedTokenIndex = newIndex;
                    row.Cells["Code"].Value = item.ExtractedCode;
                }
            }
            else if (e.ColumnIndex == dgvLinks.Columns["IsCasing"].Index)
            {
                item.IsCasing = Convert.ToBoolean(row.Cells["IsCasing"].Value);
            }
            else if (e.ColumnIndex == dgvLinks.Columns["IsOpening"].Index)
            {
                item.IsOpening = Convert.ToBoolean(row.Cells["IsOpening"].Value);
            }

            RefreshOrderLists();
        }

        private void RefreshOrderLists()
        {
            SyncOrderList(lbCasingOrder, ConfigItems.Where(x => x.IsCasing).Select(x => x.ExtractedCode).Distinct());
            SyncOrderList(lbOpeningOrder, ConfigItems.Where(x => x.IsOpening).Select(x => x.ExtractedCode).Distinct());
        }

        private void SyncOrderList(ListBox listBox, IEnumerable<string> currentCodes)
        {
            var currentList = currentCodes.Where(c => !string.IsNullOrEmpty(c)).ToList();
            List<string> existingItems = listBox.Items.Cast<string>().ToList();

            // 保留既有順序，移除被取消勾選的，加入新勾選的
            var updated = existingItems.Where(x => currentList.Contains(x)).ToList();
            foreach (var code in currentList)
            {
                if (!updated.Contains(code))
                {
                    updated.Add(code);
                }
            }

            listBox.Items.Clear();
            foreach (var item in updated) listBox.Items.Add(item);
        }

        private void MoveItem(ListBox listBox, int direction)
        {
            if (listBox.SelectedItem == null || listBox.SelectedIndex < 0) return;
            int newIndex = listBox.SelectedIndex + direction;
            if (newIndex < 0 || newIndex >= listBox.Items.Count) return;

            object selected = listBox.SelectedItem;
            listBox.Items.Remove(selected);
            listBox.Items.Insert(newIndex, selected);
            listBox.SetSelected(newIndex, true);
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            ResultSettings = new NumberingExecutionSettings
            {
                OrderedCasingCodes = lbCasingOrder.Items.Cast<string>().ToList(),
                OrderedOpeningCodes = lbOpeningOrder.Items.Cast<string>().ToList()
            };

            foreach (var item in ConfigItems)
            {
                if (!string.IsNullOrEmpty(item.ExtractedCode) && !ResultSettings.CodeToLinkMap.ContainsKey(item.ExtractedCode))
                {
                    ResultSettings.CodeToLinkMap[item.ExtractedCode] = item.LinkInstance;
                }
            }
        }

        private static List<string> ParseTokens(string rawFileName)
        {
            if (string.IsNullOrWhiteSpace(rawFileName)) return new List<string>();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars) rawFileName = rawFileName.Replace(c, ' ');
            return Regex.Split(rawFileName.Trim(), @"[\-_.\s#]+").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }
    }
}