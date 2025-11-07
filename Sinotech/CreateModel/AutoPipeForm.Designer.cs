namespace Sinotech.CreateModel
{
    partial class AutoPipeForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(AutoPipeForm));
            this.pipeSystemTypeCB = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.pipeTypeCB = new System.Windows.Forms.ComboBox();
            this.label2 = new System.Windows.Forms.Label();
            this.levelCB = new System.Windows.Forms.ComboBox();
            this.label3 = new System.Windows.Forms.Label();
            this.sureBtn = new System.Windows.Forms.Button();
            this.cancelBtn = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // pipeSystemTypeCB
            // 
            this.pipeSystemTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.pipeSystemTypeCB.FormattingEnabled = true;
            this.pipeSystemTypeCB.Location = new System.Drawing.Point(12, 33);
            this.pipeSystemTypeCB.Name = "pipeSystemTypeCB";
            this.pipeSystemTypeCB.Size = new System.Drawing.Size(246, 25);
            this.pipeSystemTypeCB.TabIndex = 0;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(98, 9);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(60, 17);
            this.label1.TabIndex = 1;
            this.label1.Text = "系統類型";
            // 
            // pipeTypeCB
            // 
            this.pipeTypeCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.pipeTypeCB.FormattingEnabled = true;
            this.pipeTypeCB.Location = new System.Drawing.Point(12, 95);
            this.pipeTypeCB.Name = "pipeTypeCB";
            this.pipeTypeCB.Size = new System.Drawing.Size(246, 25);
            this.pipeTypeCB.TabIndex = 0;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(105, 73);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(47, 17);
            this.label2.TabIndex = 1;
            this.label2.Text = "管類型";
            // 
            // levelCB
            // 
            this.levelCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.levelCB.FormattingEnabled = true;
            this.levelCB.Location = new System.Drawing.Point(12, 158);
            this.levelCB.Name = "levelCB";
            this.levelCB.Size = new System.Drawing.Size(246, 25);
            this.levelCB.TabIndex = 0;
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(108, 136);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(40, 17);
            this.label3.TabIndex = 1;
            this.label3.Text = "Level";
            // 
            // sureBtn
            // 
            this.sureBtn.Location = new System.Drawing.Point(13, 203);
            this.sureBtn.Name = "sureBtn";
            this.sureBtn.Size = new System.Drawing.Size(75, 34);
            this.sureBtn.TabIndex = 2;
            this.sureBtn.Text = "確定";
            this.sureBtn.UseVisualStyleBackColor = true;
            this.sureBtn.Click += new System.EventHandler(this.sureBtn_Click);
            // 
            // cancelBtn
            // 
            this.cancelBtn.Location = new System.Drawing.Point(183, 203);
            this.cancelBtn.Name = "cancelBtn";
            this.cancelBtn.Size = new System.Drawing.Size(75, 34);
            this.cancelBtn.TabIndex = 2;
            this.cancelBtn.Text = "取消";
            this.cancelBtn.UseVisualStyleBackColor = true;
            this.cancelBtn.Click += new System.EventHandler(this.cancelBtn_Click);
            // 
            // AutoPipeForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(270, 253);
            this.Controls.Add(this.cancelBtn);
            this.Controls.Add(this.sureBtn);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.levelCB);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.pipeTypeCB);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.pipeSystemTypeCB);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.Name = "AutoPipeForm";
            this.Text = "自動翻管";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox pipeSystemTypeCB;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox pipeTypeCB;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ComboBox levelCB;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button sureBtn;
        private System.Windows.Forms.Button cancelBtn;
    }
}