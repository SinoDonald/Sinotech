using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace Sinotech_2025.CSDSEM
{
    public partial class AutoNumberSettingForm : System.Windows.Forms.Form
    {
        public List<LinkConfigItem> ConfigItems { get; private set; } = new List<LinkConfigItem>();
        public NumberingExecutionSettings ResultSettings { get; private set; }

        private bool isBatchUpdating = false;

        public AutoNumberSettingForm()
        {
            InitializeComponent();
            RegisterEvents();
        }

        public AutoNumberSettingForm(List<RevitLinkInstance> linkInstances) : this()
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime || linkInstances == null)
            {
                return;
            }

            LoadData(linkInstances);
        }

        private void RegisterEvents()
        {
            cbGlobalTokenIndex.SelectedIndexChanged += CbGlobalTokenIndex_SelectedIndexChanged;

            dgvLinks.CellValueChanged += DgvLinks_CellValueChanged;
            dgvLinks.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (dgvLinks.IsCurrentCellDirty)
                {
                    dgvLinks.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };

            btnOpeningUp.Click += (s, e) => MoveItem(lbOpeningOrder, -1);
            btnOpeningDown.Click += (s, e) => MoveItem(lbOpeningOrder, 1);
            btnCasingUp.Click += (s, e) => MoveItem(lbCasingOrder, -1);
            btnCasingDown.Click += (s, e) => MoveItem(lbCasingOrder, 1);
            btnOk.Click += BtnOk_Click;
        }

        private void LoadData(List<RevitLinkInstance> linkInstances)
        {
            ConfigItems.Clear();
            int maxTokenCount = 0;

            foreach (var link in linkInstances)
            {
                Document linkDoc = link.GetLinkDocument();
                string rawName = linkDoc != null && !string.IsNullOrEmpty(linkDoc.PathName)
                    ? Path.GetFileNameWithoutExtension(linkDoc.PathName)
                    : link.Name.Split(':')[0].Trim();

                string cleanName = Path.GetFileNameWithoutExtension(rawName);
                List<string> tokens = ParseTokens(cleanName);
                if (tokens.Count > maxTokenCount) maxTokenCount = tokens.Count;

                // 預設解析欄位 2
                int defaultIndex = tokens.Count > 2 ? 2 : (tokens.Count > 1 ? 1 : 0);
                string code = tokens.Count > defaultIndex ? tokens[defaultIndex] : string.Empty;

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

            // 初始化全域解析欄位下拉清單
            isBatchUpdating = true;
            cbGlobalTokenIndex.Items.Clear();
            int limit = Math.Max(maxTokenCount, 4);
            for (int i = 0; i < limit; i++)
            {
                cbGlobalTokenIndex.Items.Add($"欄位 {i}");
            }
            if (cbGlobalTokenIndex.Items.Count > 2)
            {
                cbGlobalTokenIndex.SelectedIndex = 2; // 預設欄位 2
            }
            else if (cbGlobalTokenIndex.Items.Count > 0)
            {
                cbGlobalTokenIndex.SelectedIndex = 0;
            }
            isBatchUpdating = false;

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
                row.Cells["colFileName"].Value = item.CleanFileName;

                var comboCell = (DataGridViewComboBoxCell)row.Cells["colTokenIndex"];
                comboCell.Items.Clear();
                for (int i = 0; i < item.Tokens.Count; i++)
                {
                    comboCell.Items.Add($"欄位 {i} ({item.Tokens[i]})");
                }

                if (item.SelectedTokenIndex < item.Tokens.Count)
                {
                    comboCell.Value = comboCell.Items[item.SelectedTokenIndex];
                }

                row.Cells["colCode"].Value = item.ExtractedCode;
                row.Cells["colOpening"].Value = item.IsOpening;
                row.Cells["colCasing"].Value = item.IsCasing;
            }
        }

        /// <summary>
        /// 全域切換解析欄位事件：連動所有 Rows 變更
        /// </summary>
        private void CbGlobalTokenIndex_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isBatchUpdating || cbGlobalTokenIndex.SelectedIndex < 0) return;

            int targetIndex = cbGlobalTokenIndex.SelectedIndex;
            isBatchUpdating = true;

            foreach (DataGridViewRow row in dgvLinks.Rows)
            {
                var item = row.Tag as LinkConfigItem;
                if (item == null) continue;

                if (targetIndex < item.Tokens.Count)
                {
                    item.SelectedTokenIndex = targetIndex;
                    var comboCell = (DataGridViewComboBoxCell)row.Cells["colTokenIndex"];
                    comboCell.Value = comboCell.Items[targetIndex];
                    row.Cells["colCode"].Value = item.ExtractedCode;
                }
            }

            isBatchUpdating = false;
            RefreshOrderLists();
        }

        private void DgvLinks_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isBatchUpdating || e.RowIndex < 0) return;
            DataGridViewRow row = dgvLinks.Rows[e.RowIndex];
            var item = row.Tag as LinkConfigItem;
            if (item == null) return;

            if (e.ColumnIndex == dgvLinks.Columns["colTokenIndex"].Index)
            {
                var comboCell = (DataGridViewComboBoxCell)row.Cells["colTokenIndex"];
                int newIndex = comboCell.Items.IndexOf(comboCell.Value);
                if (newIndex >= 0)
                {
                    item.SelectedTokenIndex = newIndex;
                    row.Cells["colCode"].Value = item.ExtractedCode;
                }
            }
            else if (e.ColumnIndex == dgvLinks.Columns["colOpening"].Index)
            {
                item.IsOpening = Convert.ToBoolean(row.Cells["colOpening"].Value);
            }
            else if (e.ColumnIndex == dgvLinks.Columns["colCasing"].Index)
            {
                item.IsCasing = Convert.ToBoolean(row.Cells["colCasing"].Value);
            }

            RefreshOrderLists();
        }

        private void RefreshOrderLists()
        {
            SyncOrderList(lbOpeningOrder, ConfigItems.Where(x => x.IsOpening).Select(x => x.ExtractedCode).Distinct());
            SyncOrderList(lbCasingOrder, ConfigItems.Where(x => x.IsCasing).Select(x => x.ExtractedCode).Distinct());
        }

        private void SyncOrderList(ListBox listBox, IEnumerable<string> currentCodes)
        {
            var currentList = currentCodes.Where(c => !string.IsNullOrEmpty(c)).ToList();
            List<string> existingItems = listBox.Items.Cast<string>().ToList();

            var updated = existingItems.Where(x => currentList.Contains(x)).ToList();
            foreach (var code in currentList)
            {
                if (!updated.Contains(code))
                {
                    updated.Add(code);
                }
            }

            listBox.Items.Clear();
            foreach (var item in updated)
            {
                listBox.Items.Add(item);
            }
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
                OrderedOpeningCodes = lbOpeningOrder.Items.Cast<string>().ToList(),
                OrderedCasingCodes = lbCasingOrder.Items.Cast<string>().ToList()
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