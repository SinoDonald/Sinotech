
namespace Sinotech_2025
{
    partial class ChooseView
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChooseView));
            treeView1 = new System.Windows.Forms.TreeView();
            cancel = new System.Windows.Forms.Button();
            sure = new System.Windows.Forms.Button();
            optionCB = new System.Windows.Forms.ComboBox();
            label1 = new System.Windows.Forms.Label();
            formatCB = new System.Windows.Forms.ComboBox();
            label2 = new System.Windows.Forms.Label();
            label3 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // treeView1
            // 
            treeView1.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            treeView1.CheckBoxes = true;
            treeView1.Location = new System.Drawing.Point(13, 65);
            treeView1.Margin = new System.Windows.Forms.Padding(4);
            treeView1.Name = "treeView1";
            treeView1.Size = new System.Drawing.Size(306, 448);
            treeView1.TabIndex = 1;
            treeView1.AfterCheck += treeView1_AfterCheck;
            // 
            // cancel
            // 
            cancel.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            cancel.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            cancel.Location = new System.Drawing.Point(247, 524);
            cancel.Name = "cancel";
            cancel.Size = new System.Drawing.Size(72, 33);
            cancel.TabIndex = 2;
            cancel.Text = "取消";
            cancel.UseVisualStyleBackColor = true;
            cancel.Click += cancel_Click;
            // 
            // sure
            // 
            sure.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right;
            sure.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, 136);
            sure.Location = new System.Drawing.Point(158, 524);
            sure.Name = "sure";
            sure.Size = new System.Drawing.Size(72, 33);
            sure.TabIndex = 3;
            sure.Text = "確定";
            sure.UseVisualStyleBackColor = true;
            sure.Click += sure_Click;
            // 
            // optionCB
            // 
            optionCB.Anchor = System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            optionCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            optionCB.FormattingEnabled = true;
            optionCB.Location = new System.Drawing.Point(13, 524);
            optionCB.Name = "optionCB";
            optionCB.Size = new System.Drawing.Size(125, 25);
            optionCB.TabIndex = 4;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new System.Drawing.Point(13, 13);
            label1.Name = "label1";
            label1.Size = new System.Drawing.Size(99, 17);
            label1.TabIndex = 5;
            label1.Text = "請選擇匯出格式";
            // 
            // formatCB
            // 
            formatCB.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            formatCB.FormattingEnabled = true;
            formatCB.Location = new System.Drawing.Point(13, 33);
            formatCB.Name = "formatCB";
            formatCB.Size = new System.Drawing.Size(99, 25);
            formatCB.TabIndex = 4;
            formatCB.SelectedIndexChanged += formatCB_SelectedIndexChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new System.Drawing.Point(13, -478);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(99, 17);
            label2.TabIndex = 5;
            label2.Text = "請選擇匯出格式";
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new System.Drawing.Point(139, 13);
            label3.Name = "label3";
            label3.Size = new System.Drawing.Size(183, 17);
            label3.TabIndex = 6;
            label3.Text = "依「圖框-電腦圖號」名稱匯出";
            // 
            // ChooseView
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(334, 566);
            Controls.Add(label3);
            Controls.Add(label2);
            Controls.Add(formatCB);
            Controls.Add(label1);
            Controls.Add(optionCB);
            Controls.Add(cancel);
            Controls.Add(sure);
            Controls.Add(treeView1);
            Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 136);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new System.Windows.Forms.Padding(4);
            MinimumSize = new System.Drawing.Size(350, 605);
            Name = "ChooseView";
            Text = "選擇匯出視圖";
            ResumeLayout(false);
            PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Button cancel;
        private System.Windows.Forms.Button sure;
        private System.Windows.Forms.ComboBox optionCB;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox formatCB;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
    }
}