namespace Sinotech_2025.CSDSEM
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
            sureBtn = new System.Windows.Forms.Button();
            cancelBtn = new System.Windows.Forms.Button();
            radioBtnPanel = new System.Windows.Forms.Panel();
            SuspendLayout();
            // 
            // sureBtn
            // 
            sureBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            sureBtn.Location = new System.Drawing.Point(139, 318);
            sureBtn.Name = "sureBtn";
            sureBtn.Size = new System.Drawing.Size(73, 36);
            sureBtn.TabIndex = 0;
            sureBtn.Text = "確定";
            sureBtn.UseVisualStyleBackColor = true;
            sureBtn.Click += sureBtn_Click;
            // 
            // cancelBtn
            // 
            cancelBtn.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancelBtn.Location = new System.Drawing.Point(231, 318);
            cancelBtn.Name = "cancelBtn";
            cancelBtn.Size = new System.Drawing.Size(73, 36);
            cancelBtn.TabIndex = 0;
            cancelBtn.Text = "取消";
            cancelBtn.UseVisualStyleBackColor = true;
            cancelBtn.Click += cancelBtn_Click;
            // 
            // radioBtnPanel
            // 
            radioBtnPanel.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            radioBtnPanel.AutoScroll = true;
            radioBtnPanel.Location = new System.Drawing.Point(13, 13);
            radioBtnPanel.Name = "radioBtnPanel";
            radioBtnPanel.Size = new System.Drawing.Size(291, 291);
            radioBtnPanel.TabIndex = 1;
            // 
            // AutoNumberForm
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(316, 366);
            Controls.Add(radioBtnPanel);
            Controls.Add(cancelBtn);
            Controls.Add(sureBtn);
            Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4);
            MinimumSize = new System.Drawing.Size(332, 405);
            Name = "AutoNumberForm";
            Text = "請選擇視圖";
            ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.Button cancelBtn;
        private System.Windows.Forms.Panel radioBtnPanel;
    }
}