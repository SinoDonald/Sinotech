namespace Sinotech
{
    partial class CreateDrawings
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(CreateDrawings));
            this.radioBtnPanel = new System.Windows.Forms.Panel();
            this.sheetPanel = new System.Windows.Forms.Panel();
            this.sureBtn = new System.Windows.Forms.Button();
            this.cancelBtn = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // radioBtnPanel
            // 
            this.radioBtnPanel.AutoScroll = true;
            this.radioBtnPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.radioBtnPanel.Location = new System.Drawing.Point(13, 5);
            this.radioBtnPanel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.radioBtnPanel.Name = "radioBtnPanel";
            this.radioBtnPanel.Size = new System.Drawing.Size(259, 390);
            this.radioBtnPanel.TabIndex = 0;
            // 
            // sheetPanel
            // 
            this.sheetPanel.AutoScroll = true;
            this.sheetPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.sheetPanel.Location = new System.Drawing.Point(280, 5);
            this.sheetPanel.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.sheetPanel.Name = "sheetPanel";
            this.sheetPanel.Size = new System.Drawing.Size(259, 390);
            this.sheetPanel.TabIndex = 1;
            // 
            // sureBtn
            // 
            this.sureBtn.Location = new System.Drawing.Point(348, 403);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(75, 31);
            this.sureBtn.TabIndex = 2;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.SureBtn_Click);
            // 
            // cancelBtn
            // 
            this.cancelBtn.Location = new System.Drawing.Point(464, 403);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(75, 31);
            this.cancelBtn.TabIndex = 2;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.CancelBtn_Click);
            // 
            // CreateDrawings
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 19F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(553, 445);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.sureBtn);
            this.Controls.Add(this.sheetPanel);
            this.Controls.Add(this.radioBtnPanel);
            this.Font = new System.Drawing.Font("微軟正黑體", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.Name = "CreateDrawings";
            this.Text = "新增圖紙";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel radioBtnPanel;
        private System.Windows.Forms.Panel sheetPanel;
        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.Button cancelBtn;
    }
}