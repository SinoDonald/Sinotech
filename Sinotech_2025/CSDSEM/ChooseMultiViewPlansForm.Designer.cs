namespace Sinotech_2025.CSDSEM
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
            cancelBtn = new System.Windows.Forms.Button();
            sureBtn = new System.Windows.Forms.Button();
            viewplansLV = new System.Windows.Forms.ListView();
            columnHeader1 = new System.Windows.Forms.ColumnHeader();
            allCancelRbtn = new System.Windows.Forms.RadioButton();
            allRbtn = new System.Windows.Forms.RadioButton();
            SuspendLayout();
            // 
            // cancelBtn
            // 
            cancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            cancelBtn.Location = new System.Drawing.Point(263, 326);
            cancelBtn.Name = "cancelBtn";
            cancelBtn.Size = new System.Drawing.Size(75, 32);
            cancelBtn.TabIndex = 16;
            cancelBtn.Text = "取消";
            cancelBtn.UseVisualStyleBackColor = true;
            cancelBtn.Click += cancelBtn_Click;
            // 
            // sureBtn
            // 
            sureBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            sureBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            sureBtn.Location = new System.Drawing.Point(167, 327);
            sureBtn.Name = "sureBtn";
            sureBtn.Size = new System.Drawing.Size(75, 32);
            sureBtn.TabIndex = 15;
            sureBtn.Text = "確定";
            sureBtn.UseVisualStyleBackColor = true;
            sureBtn.Click += sureBtn_Click;
            // 
            // viewplansLV
            // 
            viewplansLV.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            viewplansLV.CheckBoxes = true;
            viewplansLV.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] { columnHeader1 });
            viewplansLV.Location = new System.Drawing.Point(12, 39);
            viewplansLV.Name = "viewplansLV";
            viewplansLV.Size = new System.Drawing.Size(326, 279);
            viewplansLV.TabIndex = 2;
            viewplansLV.UseCompatibleStateImageBehavior = false;
            viewplansLV.View = System.Windows.Forms.View.SmallIcon;
            viewplansLV.SelectedIndexChanged += viewplanLV_SelectedIndexChanged;
            // 
            // columnHeader1
            // 
            columnHeader1.Text = "元件名稱";
            // 
            // allCancelRbtn
            // 
            allCancelRbtn.AutoSize = true;
            allCancelRbtn.Location = new System.Drawing.Point(70, 12);
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
            allRbtn.Location = new System.Drawing.Point(12, 12);
            allRbtn.Name = "allRbtn";
            allRbtn.Size = new System.Drawing.Size(52, 21);
            allRbtn.TabIndex = 1;
            allRbtn.TabStop = true;
            allRbtn.Text = "全選";
            allRbtn.UseVisualStyleBackColor = true;
            allRbtn.CheckedChanged += allRbtn_CheckedChanged;
            // 
            // ChooseMultiViewPlansForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(350, 368);
            Controls.Add(viewplansLV);
            Controls.Add(allCancelRbtn);
            Controls.Add(cancelBtn);
            Controls.Add(allRbtn);
            Controls.Add(sureBtn);
            Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 136);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4);
            Name = "ChooseMultiViewPlansForm";
            Text = "請選擇要編輯的視圖";
            ResumeLayout(false);
            PerformLayout();

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