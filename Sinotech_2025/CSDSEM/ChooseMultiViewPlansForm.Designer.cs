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
            label1 = new System.Windows.Forms.Label();
            textBox1 = new System.Windows.Forms.TextBox();
            label2 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            textBox2 = new System.Windows.Forms.TextBox();
            label4 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // cancelBtn
            // 
            cancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            cancelBtn.Location = new System.Drawing.Point(307, 355);
            cancelBtn.Name = "cancelBtn";
            cancelBtn.Size = new System.Drawing.Size(75, 32);
            cancelBtn.TabIndex = 11;
            cancelBtn.Text = "取消";
            cancelBtn.UseVisualStyleBackColor = true;
            cancelBtn.Click += cancelBtn_Click;
            // 
            // sureBtn
            // 
            sureBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            sureBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            sureBtn.Location = new System.Drawing.Point(226, 355);
            sureBtn.Name = "sureBtn";
            sureBtn.Size = new System.Drawing.Size(75, 32);
            sureBtn.TabIndex = 10;
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
            viewplansLV.Size = new System.Drawing.Size(370, 280);
            viewplansLV.TabIndex = 3;
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
            allCancelRbtn.TabIndex = 2;
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
            // label1
            // 
            label1.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(12, 327);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(99, 17);
            label1.TabIndex = 4;
            label1.Text = "大於此長度必標";
            // 
            // textBox1
            // 
            textBox1.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            textBox1.Location = new System.Drawing.Point(114, 324);
            textBox1.Name = "textBox1";
            textBox1.Size = new System.Drawing.Size(48, 25);
            textBox1.TabIndex = 5;
            textBox1.Text = "10.0";
            textBox1.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            textBox1.TextChanged += textBox1_TextChanged;
            textBox1.KeyPress += textBox1_KeyPress;
            // 
            // label2
            // 
            label2.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(168, 327);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(20, 17);
            label2.TabIndex = 6;
            label2.Text = "m";
            // 
            // label3
            // 
            label3.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(362, 327);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(20, 17);
            label3.TabIndex = 9;
            label3.Text = "m";
            // 
            // textBox2
            // 
            textBox2.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            textBox2.Location = new System.Drawing.Point(308, 324);
            textBox2.Name = "textBox2";
            textBox2.Size = new System.Drawing.Size(48, 25);
            textBox2.TabIndex = 8;
            textBox2.Text = "2.0";
            textBox2.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            textBox2.TextChanged += textBox1_TextChanged;
            textBox2.KeyPress += textBox1_KeyPress;
            // 
            // label4
            // 
            label4.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            label4.AutoSize = true;
            label4.Location = new System.Drawing.Point(206, 327);
            label4.Name = "label4";
            label4.Size = new System.Drawing.Size(99, 17);
            label4.TabIndex = 7;
            label4.Text = "小於此長度不標";
            // 
            // ChooseMultiViewPlansForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(394, 396);
            Controls.Add(label3);
            Controls.Add(textBox2);
            Controls.Add(label4);
            Controls.Add(label2);
            Controls.Add(textBox1);
            Controls.Add(label1);
            Controls.Add(viewplansLV);
            Controls.Add(allCancelRbtn);
            Controls.Add(cancelBtn);
            Controls.Add(allRbtn);
            Controls.Add(sureBtn);
            Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 136);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4);
            MinimumSize = new System.Drawing.Size(410, 435);
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
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TextBox textBox1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox textBox2;
        private System.Windows.Forms.Label label4;
    }
}