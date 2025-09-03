namespace Sinotech
{
    partial class ChooseElems
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChooseElems));
            this.sureBtn = new System.Windows.Forms.Button();
            this.cancelBtn = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.allRbtn = new System.Windows.Forms.RadioButton();
            this.allCancelRbtn = new System.Windows.Forms.RadioButton();
            this.familyLV = new System.Windows.Forms.ListView();
            this.columnHeader1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // sureBtn
            // 
            this.sureBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.sureBtn.Location = new System.Drawing.Point(672, 523);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(75, 36);
            this.sureBtn.TabIndex = 1;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.sureBtn_Click);
            // 
            // cancelBtn
            // 
            this.cancelBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelBtn.Location = new System.Drawing.Point(763, 523);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(75, 36);
            this.cancelBtn.TabIndex = 1;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.cancelBtn_Click);
            // 
            // groupBox1
            // 
            this.groupBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBox1.Controls.Add(this.familyLV);
            this.groupBox1.Controls.Add(this.allCancelRbtn);
            this.groupBox1.Controls.Add(this.allRbtn);
            this.groupBox1.Location = new System.Drawing.Point(13, 13);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(825, 504);
            this.groupBox1.TabIndex = 2;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "請選擇要鎖定的族群";
            // 
            // allRbtn
            // 
            this.allRbtn.AutoSize = true;
            this.allRbtn.Checked = true;
            this.allRbtn.Location = new System.Drawing.Point(7, 25);
            this.allRbtn.Name = "allRbtn";
            this.allRbtn.Size = new System.Drawing.Size(52, 21);
            this.allRbtn.TabIndex = 1;
            this.allRbtn.TabStop = true;
            this.allRbtn.Text = "全選";
            this.allRbtn.UseVisualStyleBackColor = true;
            this.allRbtn.CheckedChanged += new System.EventHandler(this.allRbtn_CheckedChanged);
            // 
            // allCancelRbtn
            // 
            this.allCancelRbtn.AutoSize = true;
            this.allCancelRbtn.Location = new System.Drawing.Point(74, 25);
            this.allCancelRbtn.Name = "allCancelRbtn";
            this.allCancelRbtn.Size = new System.Drawing.Size(78, 21);
            this.allCancelRbtn.TabIndex = 1;
            this.allCancelRbtn.Text = "全部取消";
            this.allCancelRbtn.UseVisualStyleBackColor = true;
            this.allCancelRbtn.CheckedChanged += new System.EventHandler(this.allCancelRbtn_CheckedChanged);
            // 
            // familyLV
            // 
            this.familyLV.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.familyLV.CheckBoxes = true;
            this.familyLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeader1});
            this.familyLV.HideSelection = false;
            this.familyLV.Location = new System.Drawing.Point(7, 53);
            this.familyLV.Name = "familyLV";
            this.familyLV.Size = new System.Drawing.Size(812, 445);
            this.familyLV.TabIndex = 2;
            this.familyLV.UseCompatibleStateImageBehavior = false;
            this.familyLV.View = System.Windows.Forms.View.SmallIcon;
            this.familyLV.SelectedIndexChanged += new System.EventHandler(this.familyLV_SelectedIndexChanged);
            // 
            // columnHeader1
            // 
            this.columnHeader1.Text = "元件名稱";
            // 
            // ChooseElems
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(850, 571);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.sureBtn);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(420, 430);
            this.Name = "ChooseElems";
            this.Text = "元件保護";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.Button cancelBtn;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.RadioButton allRbtn;
        private System.Windows.Forms.RadioButton allCancelRbtn;
        private System.Windows.Forms.ListView familyLV;
        private System.Windows.Forms.ColumnHeader columnHeader1;
    }
}