namespace Sinotech.CSDSEM
{
    partial class AutoNumberSettingForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 設計工具產生的程式碼

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AutoNumberSettingForm));
            this.mainLayout = new System.Windows.Forms.TableLayoutPanel();
            this.topPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.lblGlobalIndex = new System.Windows.Forms.Label();
            this.cbGlobalTokenIndex = new System.Windows.Forms.ComboBox();
            this.lblGlobalTip = new System.Windows.Forms.Label();
            this.dgvLinks = new System.Windows.Forms.DataGridView();
            this.colFileName = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colTokenIndex = new System.Windows.Forms.DataGridViewComboBoxColumn();
            this.colCode = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colOpening = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.colCasing = new System.Windows.Forms.DataGridViewCheckBoxColumn();
            this.rightPanel = new System.Windows.Forms.TableLayoutPanel();
            this.gbOpening = new System.Windows.Forms.GroupBox();
            this.lbOpeningOrder = new System.Windows.Forms.ListBox();
            this.btnOpeningUp = new System.Windows.Forms.Button();
            this.btnOpeningDown = new System.Windows.Forms.Button();
            this.gbCasing = new System.Windows.Forms.GroupBox();
            this.lbCasingOrder = new System.Windows.Forms.ListBox();
            this.btnCasingUp = new System.Windows.Forms.Button();
            this.btnCasingDown = new System.Windows.Forms.Button();
            this.bottomPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.btnCancel = new System.Windows.Forms.Button();
            this.btnOk = new System.Windows.Forms.Button();
            this.mainLayout.SuspendLayout();
            this.topPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvLinks)).BeginInit();
            this.rightPanel.SuspendLayout();
            this.gbOpening.SuspendLayout();
            this.gbCasing.SuspendLayout();
            this.bottomPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // mainLayout
            // 
            this.mainLayout.ColumnCount = 2;
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65F));
            this.mainLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
            this.mainLayout.Controls.Add(this.topPanel, 0, 0);
            this.mainLayout.Controls.Add(this.dgvLinks, 0, 1);
            this.mainLayout.Controls.Add(this.rightPanel, 1, 1);
            this.mainLayout.Controls.Add(this.bottomPanel, 1, 2);
            this.mainLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayout.Location = new System.Drawing.Point(0, 0);
            this.mainLayout.Name = "mainLayout";
            this.mainLayout.Padding = new System.Windows.Forms.Padding(10);
            this.mainLayout.RowCount = 3;
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 45F));
            this.mainLayout.Size = new System.Drawing.Size(984, 561);
            this.mainLayout.TabIndex = 0;
            // 
            // topPanel
            // 
            this.mainLayout.SetColumnSpan(this.topPanel, 2);
            this.topPanel.Controls.Add(this.lblGlobalIndex);
            this.topPanel.Controls.Add(this.cbGlobalTokenIndex);
            this.topPanel.Controls.Add(this.lblGlobalTip);
            this.topPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.topPanel.Location = new System.Drawing.Point(13, 13);
            this.topPanel.Name = "topPanel";
            this.topPanel.Size = new System.Drawing.Size(958, 34);
            this.topPanel.TabIndex = 0;
            // 
            // lblGlobalIndex
            // 
            this.lblGlobalIndex.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblGlobalIndex.AutoSize = true;
            this.lblGlobalIndex.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.lblGlobalIndex.Location = new System.Drawing.Point(3, 4);
            this.lblGlobalIndex.Name = "lblGlobalIndex";
            this.lblGlobalIndex.Size = new System.Drawing.Size(125, 17);
            this.lblGlobalIndex.TabIndex = 0;
            this.lblGlobalIndex.Text = "全域預設解析欄位：";
            // 
            // cbGlobalTokenIndex
            // 
            this.cbGlobalTokenIndex.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.cbGlobalTokenIndex.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cbGlobalTokenIndex.FormattingEnabled = true;
            this.cbGlobalTokenIndex.Location = new System.Drawing.Point(134, 3);
            this.cbGlobalTokenIndex.Name = "cbGlobalTokenIndex";
            this.cbGlobalTokenIndex.Size = new System.Drawing.Size(130, 25);
            this.cbGlobalTokenIndex.TabIndex = 1;
            // 
            // lblGlobalTip
            // 
            this.lblGlobalTip.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.lblGlobalTip.AutoSize = true;
            this.lblGlobalTip.ForeColor = System.Drawing.Color.DimGray;
            this.lblGlobalTip.Location = new System.Drawing.Point(270, 4);
            this.lblGlobalTip.Name = "lblGlobalTip";
            this.lblGlobalTip.Size = new System.Drawing.Size(302, 17);
            this.lblGlobalTip.TabIndex = 2;
            this.lblGlobalTip.Text = "(切換後將統一變更所有檔案之欄位，可再個別微調)";
            // 
            // dgvLinks
            // 
            this.dgvLinks.AllowUserToAddRows = false;
            this.dgvLinks.AllowUserToDeleteRows = false;
            this.dgvLinks.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dgvLinks.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colFileName,
            this.colTokenIndex,
            this.colCode,
            this.colOpening,
            this.colCasing});
            this.dgvLinks.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dgvLinks.Location = new System.Drawing.Point(13, 53);
            this.dgvLinks.Name = "dgvLinks";
            this.dgvLinks.RowHeadersVisible = false;
            this.dgvLinks.RowTemplate.Height = 26;
            this.dgvLinks.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.dgvLinks.Size = new System.Drawing.Size(620, 450);
            this.dgvLinks.TabIndex = 1;
            // 
            // colFileName
            // 
            this.colFileName.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.colFileName.HeaderText = "連結模型檔名";
            this.colFileName.MinimumWidth = 200;
            this.colFileName.Name = "colFileName";
            this.colFileName.ReadOnly = true;
            this.colFileName.Width = 200;
            // 
            // colTokenIndex
            // 
            this.colTokenIndex.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.colTokenIndex.HeaderText = "解析欄位";
            this.colTokenIndex.MinimumWidth = 120;
            this.colTokenIndex.Name = "colTokenIndex";
            this.colTokenIndex.Width = 120;
            // 
            // colCode
            // 
            this.colCode.AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.AllCells;
            this.colCode.HeaderText = "代碼";
            this.colCode.MinimumWidth = 70;
            this.colCode.Name = "colCode";
            this.colCode.ReadOnly = true;
            this.colCode.Width = 70;
            // 
            // colOpening
            // 
            this.colOpening.HeaderText = "開口";
            this.colOpening.MinimumWidth = 55;
            this.colOpening.Name = "colOpening";
            this.colOpening.Width = 60;
            // 
            // colCasing
            // 
            this.colCasing.HeaderText = "套管";
            this.colCasing.MinimumWidth = 55;
            this.colCasing.Name = "colCasing";
            this.colCasing.Width = 60;
            // 
            // rightPanel
            // 
            this.rightPanel.ColumnCount = 1;
            this.rightPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightPanel.Controls.Add(this.gbOpening, 0, 0);
            this.rightPanel.Controls.Add(this.gbCasing, 0, 1);
            this.rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightPanel.Location = new System.Drawing.Point(639, 53);
            this.rightPanel.Name = "rightPanel";
            this.rightPanel.RowCount = 2;
            this.rightPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.rightPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.rightPanel.Size = new System.Drawing.Size(332, 450);
            this.rightPanel.TabIndex = 2;
            // 
            // gbOpening
            // 
            this.gbOpening.Controls.Add(this.lbOpeningOrder);
            this.gbOpening.Controls.Add(this.btnOpeningUp);
            this.gbOpening.Controls.Add(this.btnOpeningDown);
            this.gbOpening.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbOpening.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.gbOpening.Location = new System.Drawing.Point(3, 3);
            this.gbOpening.Name = "gbOpening";
            this.gbOpening.Padding = new System.Windows.Forms.Padding(6);
            this.gbOpening.Size = new System.Drawing.Size(326, 219);
            this.gbOpening.TabIndex = 0;
            this.gbOpening.TabStop = false;
            this.gbOpening.Text = "【1. 開口編號順序】(由上至下)";
            // 
            // lbOpeningOrder
            // 
            this.lbOpeningOrder.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lbOpeningOrder.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.lbOpeningOrder.FormattingEnabled = true;
            this.lbOpeningOrder.ItemHeight = 17;
            this.lbOpeningOrder.Location = new System.Drawing.Point(9, 24);
            this.lbOpeningOrder.Name = "lbOpeningOrder";
            this.lbOpeningOrder.Size = new System.Drawing.Size(215, 174);
            this.lbOpeningOrder.TabIndex = 0;
            // 
            // btnOpeningUp
            // 
            this.btnOpeningUp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpeningUp.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnOpeningUp.Location = new System.Drawing.Point(233, 24);
            this.btnOpeningUp.Name = "btnOpeningUp";
            this.btnOpeningUp.Size = new System.Drawing.Size(84, 32);
            this.btnOpeningUp.TabIndex = 1;
            this.btnOpeningUp.Text = "▲ 上移";
            this.btnOpeningUp.UseVisualStyleBackColor = true;
            // 
            // btnOpeningDown
            // 
            this.btnOpeningDown.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpeningDown.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnOpeningDown.Location = new System.Drawing.Point(233, 65);
            this.btnOpeningDown.Name = "btnOpeningDown";
            this.btnOpeningDown.Size = new System.Drawing.Size(84, 32);
            this.btnOpeningDown.TabIndex = 2;
            this.btnOpeningDown.Text = "▼ 下移";
            this.btnOpeningDown.UseVisualStyleBackColor = true;
            // 
            // gbCasing
            // 
            this.gbCasing.Controls.Add(this.lbCasingOrder);
            this.gbCasing.Controls.Add(this.btnCasingUp);
            this.gbCasing.Controls.Add(this.btnCasingDown);
            this.gbCasing.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gbCasing.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.gbCasing.Location = new System.Drawing.Point(3, 228);
            this.gbCasing.Name = "gbCasing";
            this.gbCasing.Padding = new System.Windows.Forms.Padding(6);
            this.gbCasing.Size = new System.Drawing.Size(326, 219);
            this.gbCasing.TabIndex = 1;
            this.gbCasing.TabStop = false;
            this.gbCasing.Text = "【2. 套管編號順序】(由上至下)";
            // 
            // lbCasingOrder
            // 
            this.lbCasingOrder.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.lbCasingOrder.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.lbCasingOrder.FormattingEnabled = true;
            this.lbCasingOrder.ItemHeight = 17;
            this.lbCasingOrder.Location = new System.Drawing.Point(9, 24);
            this.lbCasingOrder.Name = "lbCasingOrder";
            this.lbCasingOrder.Size = new System.Drawing.Size(215, 174);
            this.lbCasingOrder.TabIndex = 0;
            // 
            // btnCasingUp
            // 
            this.btnCasingUp.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCasingUp.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnCasingUp.Location = new System.Drawing.Point(233, 24);
            this.btnCasingUp.Name = "btnCasingUp";
            this.btnCasingUp.Size = new System.Drawing.Size(84, 32);
            this.btnCasingUp.TabIndex = 1;
            this.btnCasingUp.Text = "▲ 上移";
            this.btnCasingUp.UseVisualStyleBackColor = true;
            // 
            // btnCasingDown
            // 
            this.btnCasingDown.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnCasingDown.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnCasingDown.Location = new System.Drawing.Point(233, 65);
            this.btnCasingDown.Name = "btnCasingDown";
            this.btnCasingDown.Size = new System.Drawing.Size(84, 32);
            this.btnCasingDown.TabIndex = 2;
            this.btnCasingDown.Text = "▼ 下移";
            this.btnCasingDown.UseVisualStyleBackColor = true;
            // 
            // bottomPanel
            // 
            this.bottomPanel.Controls.Add(this.btnCancel);
            this.bottomPanel.Controls.Add(this.btnOk);
            this.bottomPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.bottomPanel.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
            this.bottomPanel.Location = new System.Drawing.Point(639, 509);
            this.bottomPanel.Name = "bottomPanel";
            this.bottomPanel.Size = new System.Drawing.Size(332, 39);
            this.bottomPanel.TabIndex = 3;
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnCancel.Location = new System.Drawing.Point(242, 3);
            this.btnCancel.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(86, 32);
            this.btnCancel.TabIndex = 0;
            this.btnCancel.Text = "取消";
            this.btnCancel.UseVisualStyleBackColor = true;
            // 
            // btnOk
            // 
            this.btnOk.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.btnOk.Font = new System.Drawing.Font("微軟正黑體", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.btnOk.Location = new System.Drawing.Point(142, 3);
            this.btnOk.Margin = new System.Windows.Forms.Padding(4, 3, 4, 3);
            this.btnOk.Name = "btnOk";
            this.btnOk.Size = new System.Drawing.Size(92, 32);
            this.btnOk.TabIndex = 1;
            this.btnOk.Text = "開始編號";
            this.btnOk.UseVisualStyleBackColor = true;
            // 
            // AutoNumberSettingForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(984, 561);
            this.Controls.Add(this.mainLayout);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(900, 520);
            this.Name = "AutoNumberSettingForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "開口與套管自動編號設定";
            this.mainLayout.ResumeLayout(false);
            this.topPanel.ResumeLayout(false);
            this.topPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dgvLinks)).EndInit();
            this.rightPanel.ResumeLayout(false);
            this.gbOpening.ResumeLayout(false);
            this.gbCasing.ResumeLayout(false);
            this.bottomPanel.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel mainLayout;
        private System.Windows.Forms.FlowLayoutPanel topPanel;
        private System.Windows.Forms.Label lblGlobalIndex;
        private System.Windows.Forms.ComboBox cbGlobalTokenIndex;
        private System.Windows.Forms.Label lblGlobalTip;
        private System.Windows.Forms.DataGridView dgvLinks;
        private System.Windows.Forms.DataGridViewTextBoxColumn colFileName;
        private System.Windows.Forms.DataGridViewComboBoxColumn colTokenIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn colCode;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colOpening;
        private System.Windows.Forms.DataGridViewCheckBoxColumn colCasing;
        private System.Windows.Forms.TableLayoutPanel rightPanel;
        private System.Windows.Forms.GroupBox gbOpening;
        private System.Windows.Forms.ListBox lbOpeningOrder;
        private System.Windows.Forms.Button btnOpeningUp;
        private System.Windows.Forms.Button btnOpeningDown;
        private System.Windows.Forms.GroupBox gbCasing;
        private System.Windows.Forms.ListBox lbCasingOrder;
        private System.Windows.Forms.Button btnCasingUp;
        private System.Windows.Forms.Button btnCasingDown;
        private System.Windows.Forms.FlowLayoutPanel bottomPanel;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Button btnOk;
    }
}