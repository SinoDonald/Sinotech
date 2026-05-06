namespace Sinotech_2020.CSDSEM
{
    partial class ChooseMultiViewPlansForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChooseMultiViewPlansForm));
            this.cancelBtn = new System.Windows.Forms.Button();
            this.sureBtn = new System.Windows.Forms.Button();
            this.viewplansLV = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.allCancelRbtn = new System.Windows.Forms.RadioButton();
            this.allRbtn = new System.Windows.Forms.RadioButton();
            this.SuspendLayout();
            // 
            // cancelBtn
            // 
            this.cancelBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.cancelBtn.Location = new System.Drawing.Point(263, 326);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(75, 32);
            this.cancelBtn.TabIndex = 16;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.cancelBtn_Click);
            // 
            // sureBtn
            // 
            this.sureBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.sureBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.sureBtn.Location = new System.Drawing.Point(167, 327);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(75, 32);
            this.sureBtn.TabIndex = 15;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.sureBtn_Click);
            // 
            // viewplansLV
            // 
            this.viewplansLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.viewplansLV.CheckBoxes = true;
            this.viewplansLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
            this.viewplansLV.HideSelection = false;
            this.viewplansLV.Location = new System.Drawing.Point(12, 39);
            this.viewplansLV.Name = "viewplansLV";
            this.viewplansLV.Size = new System.Drawing.Size(326, 279);
            this.viewplansLV.TabIndex = 2;
            this.viewplansLV.UseCompatibleStateImageBehavior = false;
            this.viewplansLV.View = System.Windows.Forms.View.SmallIcon;
            this.viewplansLV.SelectedIndexChanged += new System.EventHandler(this.viewplanLV_SelectedIndexChanged);
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "元件名稱";
            // 
            // allCancelRbtn
            // 
            this.allCancelRbtn.AutoSize = true;
            this.allCancelRbtn.Location = new System.Drawing.Point(70, 12);
            this.allCancelRbtn.Name = "allCancelRbtn";
            this.allCancelRbtn.Size = new System.Drawing.Size(78, 21);
            this.allCancelRbtn.TabIndex = 1;
            this.allCancelRbtn.Text = "全部取消";
            this.allCancelRbtn.UseVisualStyleBackColor = true;
            this.allCancelRbtn.CheckedChanged += new System.EventHandler(this.allCancelRbtn_CheckedChanged);
            // 
            // allRbtn
            // 
            this.allRbtn.AutoSize = true;
            this.allRbtn.Checked = true;
            this.allRbtn.Location = new System.Drawing.Point(12, 12);
            this.allRbtn.Name = "allRbtn";
            this.allRbtn.Size = new System.Drawing.Size(52, 21);
            this.allRbtn.TabIndex = 1;
            this.allRbtn.TabStop = true;
            this.allRbtn.Text = "全選";
            this.allRbtn.UseVisualStyleBackColor = true;
            this.allRbtn.CheckedChanged += new System.EventHandler(this.allRbtn_CheckedChanged);
            // 
            // ChooseMultiViewPlansForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(350, 368);
            this.Controls.Add(this.viewplansLV);
            this.Controls.Add(this.allCancelRbtn);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.allRbtn);
            this.Controls.Add(this.sureBtn);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "ChooseMultiViewPlansForm";
            this.Text = "請選擇要編輯的視圖";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button cancelBtn;
        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.ListView viewplansLV;
        private System.Windows.Forms.ColumnHeader columnHeader1;
        private System.Windows.Forms.RadioButton allCancelRbtn;
        private System.Windows.Forms.RadioButton allRbtn;
    }
}