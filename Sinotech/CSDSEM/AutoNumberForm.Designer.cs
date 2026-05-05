namespace AutoSign.AutoNumber
{
    partial class AutoNumberForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AutoNumberForm));
            this.sureBtn = new System.Windows.Forms.Button();
            this.cancelBtn = new System.Windows.Forms.Button();
            this.radioBtnPanel = new System.Windows.Forms.Panel();
            this.SuspendLayout();
            // 
            // sureBtn
            // 
            this.sureBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.sureBtn.Location = new System.Drawing.Point(139, 318);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(73, 36);
            this.sureBtn.TabIndex = 0;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.sureBtn_Click);
            // 
            // cancelBtn
            // 
            this.cancelBtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancelBtn.Location = new System.Drawing.Point(231, 318);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(73, 36);
            this.cancelBtn.TabIndex = 0;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.cancelBtn_Click);
            // 
            // radioBtnPanel
            // 
            this.radioBtnPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.radioBtnPanel.AutoScroll = true;
            this.radioBtnPanel.Location = new System.Drawing.Point(13, 13);
            this.radioBtnPanel.Name = "radioBtnPanel";
            this.radioBtnPanel.Size = new System.Drawing.Size(291, 291);
            this.radioBtnPanel.TabIndex = 1;
            // 
            // AutoNumberForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(316, 366);
            this.Controls.Add(this.radioBtnPanel);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.sureBtn);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(332, 405);
            this.Name = "AutoNumberForm";
            this.Text = "請選擇視圖";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.Button cancelBtn;
        private System.Windows.Forms.Panel radioBtnPanel;
    }
}