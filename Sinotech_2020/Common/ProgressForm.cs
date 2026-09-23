using System;

namespace Sinotech_2020.Common
{
    public class ProgressForm : System.Windows.Forms.Form
    {
        private System.Windows.Forms.Label _labelTitle;
        private System.Windows.Forms.Label _labelCurrent;
        private System.Windows.Forms.ProgressBar _progressBar;
        private System.Windows.Forms.Label _labelPercent;

        private readonly int _total;
        private int _current = 0;

        public ProgressForm(string title, int totalCount)
        {
            _total = Math.Max(1, totalCount);
            InitializeComponents(title);
        }

        private void InitializeComponents(string title)
        {
            this.Text = title;
            this.Width = 420;
            this.Height = 150;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.ControlBox = false; // 不顯示關閉按鈕，避免被誤關

            _labelTitle = new System.Windows.Forms.Label
            {
                Text = title,
                Left = 12,
                Top = 10,
                Width = 390,
                Font = new System.Drawing.Font("微軟正黑體", 10, System.Drawing.FontStyle.Bold)
            };

            _labelCurrent = new System.Windows.Forms.Label
            {
                Text = "準備中...",
                Left = 12,
                Top = 35,
                Width = 390,
                Font = new System.Drawing.Font("微軟正黑體", 9)
            };

            _progressBar = new System.Windows.Forms.ProgressBar
            {
                Left = 12,
                Top = 60,
                Width = 390,
                Height = 22,
                Minimum = 0,
                Maximum = _total,
                Value = 0,
                Style = System.Windows.Forms.ProgressBarStyle.Continuous
            };

            _labelPercent = new System.Windows.Forms.Label
            {
                Text = $"0 / {_total}（0%）",
                Left = 12,
                Top = 88,
                Width = 390,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Font = new System.Drawing.Font("微軟正黑體", 9)
            };

            this.Controls.Add(_labelTitle);
            this.Controls.Add(_labelCurrent);
            this.Controls.Add(_progressBar);
            this.Controls.Add(_labelPercent);
        }

        /// <summary>推進一格並顯示目前處理的視圖名稱。</summary>
        public void UpdateProgress(string currentViewName)
        {
            _current++;
            int pct = (int)Math.Round(_current * 100.0 / _total);
            _labelCurrent.Text = $"處理中：{currentViewName}";
            _progressBar.Value = Math.Min(_current, _total);
            _labelPercent.Text = $"{_current} / {_total}（{pct}%）";
        }
    }
}
