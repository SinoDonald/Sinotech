namespace Sinotech_2025
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
            sureBtn = new System.Windows.Forms.Button();
            cancelBtn = new System.Windows.Forms.Button();
            groupBox1 = new System.Windows.Forms.GroupBox();
            familyLV = new System.Windows.Forms.ListView();
            columnHeader1 = new System.Windows.Forms.ColumnHeader();
            allCancelRbtn = new System.Windows.Forms.RadioButton();
            allRbtn = new System.Windows.Forms.RadioButton();
            groupBox1.SuspendLayout();
            SuspendLayout();
            // 
            // sureBtn
            // 
            sureBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            sureBtn.Location = new System.Drawing.Point(226, 463);
            sureBtn.Name = "sureBtn";
            sureBtn.Size = new System.Drawing.Size(75, 36);
            sureBtn.TabIndex = 1;
            sureBtn.Text = "確定";
            sureBtn.UseVisualStyleBackColor = true;
            sureBtn.Click += sureBtn_Click;
            // 
            // cancelBtn
            // 
            cancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelBtn.Location = new System.Drawing.Point(317, 463);
            cancelBtn.Name = "cancelBtn";
            cancelBtn.Size = new System.Drawing.Size(75, 36);
            cancelBtn.TabIndex = 1;
            cancelBtn.Text = "取消";
            cancelBtn.UseVisualStyleBackColor = true;
            cancelBtn.Click += cancelBtn_Click;
            // 
            // groupBox1
            // 
            groupBox1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            groupBox1.Controls.Add(familyLV);
            groupBox1.Controls.Add(allCancelRbtn);
            groupBox1.Controls.Add(allRbtn);
            groupBox1.Location = new System.Drawing.Point(13, 13);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new System.Drawing.Size(379, 444);
            groupBox1.TabIndex = 2;
            groupBox1.TabStop = false;
            groupBox1.Text = "請選擇要鎖定的族群";
            // 
            // familyLV
            // 
            familyLV.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            familyLV.CheckBoxes = true;
            familyLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { columnHeader1 });
            familyLV.Location = new System.Drawing.Point(7, 53);
            familyLV.Name = "familyLV";
            familyLV.Size = new System.Drawing.Size(366, 385);
            familyLV.TabIndex = 2;
            familyLV.UseCompatibleStateImageBehavior = false;
            familyLV.View = System.Windows.Forms.View.SmallIcon;
            familyLV.SelectedIndexChanged += familyLV_SelectedIndexChanged;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "元件名稱";
            // 
            // allCancelRbtn
            // 
            allCancelRbtn.AutoSize = true;
            allCancelRbtn.Location = new System.Drawing.Point(74, 25);
            allCancelRbtn.Name = "allCancelRbtn";
            allCancelRbtn.Size = new System.Drawing.Size(78, 21);
            allCancelRbtn.TabIndex = 1;
            allCancelRbtn.Text = "全部取消";
            allCancelRbtn.UseVisualStyleBackColor = true;
            allCancelRbtn.CheckedChanged += allCancelRbtn_CheckedChanged;
            // 
            // allRbtn
            // 
            allRbtn.AutoSize = true;
            allRbtn.Checked = true;
            allRbtn.Location = new System.Drawing.Point(7, 25);
            allRbtn.Name = "allRbtn";
            allRbtn.Size = new System.Drawing.Size(52, 21);
            allRbtn.TabIndex = 1;
            allRbtn.TabStop = true;
            allRbtn.Text = "全選";
            allRbtn.UseVisualStyleBackColor = true;
            allRbtn.CheckedChanged += allRbtn_CheckedChanged;
            // 
            // ChooseElems
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(404, 511);
            Controls.Add(groupBox1);
            Controls.Add(cancelBtn);
            Controls.Add(sureBtn);
            Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4);
            MinimumSize = new System.Drawing.Size(420, 430);
            Name = "ChooseElems";
            Text = "元件保護";
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ResumeLayout(false);

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