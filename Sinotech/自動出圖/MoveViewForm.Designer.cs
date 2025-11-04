namespace Sinotech
{
    partial class MoveViewForm
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MoveViewForm));
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.treeView2 = new System.Windows.Forms.TreeView();
            this.sure = new System.Windows.Forms.Button();
            this.cancel = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // treeView1
            // 
            this.treeView1.CheckBoxes = true;
            this.treeView1.Location = new System.Drawing.Point(12, 12);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(280, 356);
            this.treeView1.TabIndex = 0;
            this.treeView1.AfterCheck += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterCheck);
            // 
            // treeView2
            // 
            this.treeView2.Anchor = System.Windows.Forms.AnchorStyles.Top;
            this.treeView2.Location = new System.Drawing.Point(307, 12);
            this.treeView2.Name = "treeView2";
            this.treeView2.Size = new System.Drawing.Size(280, 356);
            this.treeView2.TabIndex = 0;
            this.treeView2.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView2_AfterSelect);
            // 
            // sure
            // 
            this.sure.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.sure.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.sure.Location = new System.Drawing.Point(426, 380);
            this.sure.Name = "sure";
            this.sure.Size = new System.Drawing.Size(72, 33);
            this.sure.TabIndex = 1;
            this.sure.Text = "確定";
            this.sure.UseVisualStyleBackColor = true;
            this.sure.Click += new System.EventHandler(this.sure_Click);
            // 
            // cancel
            // 
            this.cancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.cancel.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.cancel.Location = new System.Drawing.Point(515, 380);
            this.cancel.Name = "cancel";
            this.cancel.Size = new System.Drawing.Size(72, 33);
            this.cancel.TabIndex = 1;
            this.cancel.Text = "取消";
            this.cancel.UseVisualStyleBackColor = true;
            this.cancel.Click += new System.EventHandler(this.cancel_Click);
            // 
            // MoveViewForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 17F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(601, 425);
            this.Controls.Add(this.cancel);
            this.Controls.Add(this.sure);
            this.Controls.Add(this.treeView2);
            this.Controls.Add(this.treeView1);
            this.Font = new System.Drawing.Font("微軟正黑體", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(136)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(617, 464);
            this.Name = "MoveViewForm";
            this.Text = "視圖搬移";
            this.Resize += new System.EventHandler(this.MoveViewForm_Resize);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.TreeView treeView2;
        private System.Windows.Forms.Button sure;
        private System.Windows.Forms.Button cancel;
    }
}