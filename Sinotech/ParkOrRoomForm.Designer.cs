namespace Sinotech
{
    partial class ParkOrRoomForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ParkOrRoomForm));
            this.parkOrRoomCB = new System.Windows.Forms.ComboBox();
            this.replaceNumber = new System.Windows.Forms.TextBox();
            this.replaceFourRB = new System.Windows.Forms.RadioButton();
            this.noFourRB = new System.Windows.Forms.RadioButton();
            this.cancelBtn = new System.Windows.Forms.Button();
            this.sureBtn = new System.Windows.Forms.Button();
            this.behindName = new System.Windows.Forms.TextBox();
            this.beforeName = new System.Windows.Forms.TextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.startNumber = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // parkOrRoomCB
            // 
            this.parkOrRoomCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.parkOrRoomCB.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.parkOrRoomCB.FormattingEnabled = true;
            this.parkOrRoomCB.Items.AddRange(new object[] {
            "停車格",
            "房間"});
            this.parkOrRoomCB.Location = new System.Drawing.Point(13, 30);
            this.parkOrRoomCB.Margin = new System.Windows.Forms.Padding(4);
            this.parkOrRoomCB.Name = "parkOrRoomCB";
            this.parkOrRoomCB.Size = new System.Drawing.Size(225, 25);
            this.parkOrRoomCB.TabIndex = 1;
            // 
            // replaceNumber
            // 
            this.replaceNumber.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.replaceNumber.Location = new System.Drawing.Point(203, 132);
            this.replaceNumber.Margin = new System.Windows.Forms.Padding(4);
            this.replaceNumber.Name = "replaceNumber";
            this.replaceNumber.Size = new System.Drawing.Size(36, 25);
            this.replaceNumber.TabIndex = 7;
            // 
            // replaceFourRB
            // 
            this.replaceFourRB.AutoSize = true;
            this.replaceFourRB.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.replaceFourRB.Location = new System.Drawing.Point(108, 136);
            this.replaceFourRB.Margin = new System.Windows.Forms.Padding(4);
            this.replaceFourRB.Name = "replaceFourRB";
            this.replaceFourRB.Size = new System.Drawing.Size(86, 21);
            this.replaceFourRB.TabIndex = 6;
            this.replaceFourRB.TabStop = true;
            this.replaceFourRB.Text = "尾數4取代";
            this.replaceFourRB.UseVisualStyleBackColor = true;
            this.replaceFourRB.CheckedChanged += new System.EventHandler(this.ReplaceFourRB_CheckedChanged);
            // 
            // noFourRB
            // 
            this.noFourRB.AutoSize = true;
            this.noFourRB.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.noFourRB.Location = new System.Drawing.Point(14, 136);
            this.noFourRB.Margin = new System.Windows.Forms.Padding(4);
            this.noFourRB.Name = "noFourRB";
            this.noFourRB.Size = new System.Drawing.Size(86, 21);
            this.noFourRB.TabIndex = 5;
            this.noFourRB.TabStop = true;
            this.noFourRB.Text = "略過尾數4";
            this.noFourRB.UseVisualStyleBackColor = true;
            this.noFourRB.CheckedChanged += new System.EventHandler(this.NoFourRB_CheckedChanged);
            // 
            // cancelBtn
            // 
            this.cancelBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.cancelBtn.Location = new System.Drawing.Point(161, 175);
            this.cancelBtn.Margin = new System.Windows.Forms.Padding(4);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(77, 32);
            this.cancelBtn.TabIndex = 9;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // sureBtn
            // 
            this.sureBtn.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.sureBtn.Location = new System.Drawing.Point(13, 175);
            this.sureBtn.Margin = new System.Windows.Forms.Padding(4);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(77, 32);
            this.sureBtn.TabIndex = 8;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.SureBtn_Click);
            // 
            // behindName
            // 
            this.behindName.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.behindName.Location = new System.Drawing.Point(186, 89);
            this.behindName.Margin = new System.Windows.Forms.Padding(4);
            this.behindName.Name = "behindName";
            this.behindName.Size = new System.Drawing.Size(53, 25);
            this.behindName.TabIndex = 4;
            this.behindName.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // beforeName
            // 
            this.beforeName.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.beforeName.Location = new System.Drawing.Point(14, 89);
            this.beforeName.Margin = new System.Windows.Forms.Padding(4);
            this.beforeName.Name = "beforeName";
            this.beforeName.Size = new System.Drawing.Size(53, 25);
            this.beforeName.TabIndex = 2;
            this.beforeName.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label4.Location = new System.Drawing.Point(196, 68);
            this.label4.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(34, 17);
            this.label4.TabIndex = 14;
            this.label4.Text = "後綴";
            // 
            // startNumber
            // 
            this.startNumber.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.startNumber.Location = new System.Drawing.Point(76, 89);
            this.startNumber.Margin = new System.Windows.Forms.Padding(4);
            this.startNumber.Name = "startNumber";
            this.startNumber.Size = new System.Drawing.Size(100, 25);
            this.startNumber.TabIndex = 3;
            this.startNumber.Text = "1";
            this.startNumber.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.startNumber.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.OnlyNumber_KeyPress);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label3.Location = new System.Drawing.Point(22, 68);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(34, 17);
            this.label3.TabIndex = 15;
            this.label3.Text = "前綴";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label2.Location = new System.Drawing.Point(61, 9);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(125, 17);
            this.label2.TabIndex = 16;
            this.label2.Text = "請選擇要編號的元件";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.label1.Location = new System.Drawing.Point(96, 68);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 17);
            this.label1.TabIndex = 17;
            this.label1.Text = "起始編號";
            // 
            // ParkOrRoomForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.ClientSize = new System.Drawing.Size(256, 220);
            this.Controls.Add(this.parkOrRoomCB);
            this.Controls.Add(this.replaceNumber);
            this.Controls.Add(this.replaceFourRB);
            this.Controls.Add(this.noFourRB);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.sureBtn);
            this.Controls.Add(this.behindName);
            this.Controls.Add(this.beforeName);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.startNumber);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.DoubleBuffered = true;
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "ParkOrRoomForm";
            this.Text = "自動編號";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox parkOrRoomCB;
        private System.Windows.Forms.TextBox replaceNumber;
        private System.Windows.Forms.RadioButton replaceFourRB;
        private System.Windows.Forms.RadioButton noFourRB;
        private System.Windows.Forms.Button cancelBtn;
        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.TextBox behindName;
        private System.Windows.Forms.TextBox beforeName;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.TextBox startNumber;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label1;
    }
}