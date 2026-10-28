using System;
using System.Drawing;
using System.Windows.Forms;

namespace DDCodexInstallerHelper
{
    internal sealed class DiagnosticReportForm : Form
    {
        internal DiagnosticReportForm(string report)
        {
            Text = "组织设置检查日志 · DD Codex " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(800, 580);
            MinimumSize = new Size(640, 420);
            Font = new Font("Microsoft YaHei UI", 9.5F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(243, 247, 252);
            MinimizeBox = false;
            ShowInTaskbar = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            Controls.Add(root);
            root.Controls.Add(new Label {
                Text = "检查结果已同步到助手运行日志。点击“复制日志”，把这份报告发回即可。\n本次包含独立官方后台读取测试；结果通过不代表桌面界面已恢复。",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(91, 107, 132)
            }, 0, 0);
            var reportBox = new RichTextBox {
                Name = "organizationReportBox", Text = report, ReadOnly = true, Dock = DockStyle.Fill,
                BackColor = Color.White, ForeColor = Color.FromArgb(20, 34, 58), Font = new Font("Microsoft YaHei UI", 9F),
                DetectUrls = false, WordWrap = true, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            root.Controls.Add(reportBox, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), Margin = new Padding(0) };
            var close = new Button { Text = "关闭", Name = "closeReportButton", Width = 90, Height = 30, DialogResult = DialogResult.Cancel };
            var copy = new Button { Text = "复制日志", Name = "copyReportButton", Width = 112, Height = 30 };
            copy.Click += (sender, args) => {
                try { Clipboard.SetText(reportBox.Text); copy.Text = "已复制"; }
                catch { MessageBox.Show(this, "剪贴板暂时不可用，请稍后重试，或在日志框中全选后复制。", "复制日志未完成", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            buttons.Controls.Add(close);
            buttons.Controls.Add(copy);
            root.Controls.Add(buttons, 0, 2);
            CancelButton = close;
        }
    }
}
