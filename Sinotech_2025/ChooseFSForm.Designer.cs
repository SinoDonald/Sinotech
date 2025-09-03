namespace Sinotech_2025
{
    partial class ChooseFSForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChooseFSForm));
            this.sure = new System.Windows.Forms.Button();
            this.radioBtnPanel = new System.Windows.Forms.Panel();
            this.cancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // sure
            // 
            this.sure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.sure.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.sure.Location = new System.Drawing.Point(12, 314);
            this.sure.Name = "sure";
            this.sure.Size = new System.Drawing.Size(75, 30);
            this.sure.TabIndex = 0;
            this.sure.Text = "確定";
            this.sure.UseVisualStyleBackColor = true;
            this.sure.Click += new System.EventHandler(this.sure_Click);
            // 
            // radioBtnPanel
            // 
            this.radioBtnPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.radioBtnPanel.AutoScroll = true;
            this.radioBtnPanel.Location = new System.Drawing.Point(2, 3);
            this.radioBtnPanel.Name = "radioBtnPanel";
            this.radioBtnPanel.Size = new System.Drawing.Size(300, 305);
            this.radioBtnPanel.TabIndex = 1;
            // 
            // cancel
            // 
            this.cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancel.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.cancel.Location = new System.Drawing.Point(217, 314);
            this.cancel.Name = "cancel";
            this.cancel.Size = new System.Drawing.Size(75, 30);
            this.cancel.TabIndex = 0;
            this.cancel.Text = "取消";
            this.cancel.UseVisualStyleBackColor = true;
            this.cancel.Click += new System.EventHandler(this.cancel_Click);
            // 
            // ChooseFSForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(304, 351);
            this.Controls.Add(this.radioBtnPanel);
            this.Controls.Add(this.cancel);
            this.Controls.Add(this.sure);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(320, 390);
            this.Name = "ChooseFSForm";
            this.Text = "圖紙選擇";
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button sure;
        private System.Windows.Forms.Panel radioBtnPanel;
        private System.Windows.Forms.Button cancel;
    }
}