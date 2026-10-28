using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("DD Codex 安装助手")]
[assembly: AssemblyDescription("DD Codex 桌面客户端安装与 API 配置工具")]
[assembly: AssemblyCompany("DD API")]
[assembly: AssemblyProduct("DD Codex Installer Helper")]
[assembly: AssemblyVersion("2.1.17.0")]
[assembly: AssemblyFileVersion("2.1.17.0")]

namespace DDCodexInstallerHelper
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
            if (OrganizationProbeSupport.HandleCommand(args)) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (ChineseMaintenanceSupport.HandleCommand(args)) return;
            Application.Run(new MainForm());
        }
    }

    public sealed class MainForm : Form
    {
        private const string ApiBaseUrl = "https://api.dingdong.work/v1";
        private const string RegisterUrl = "https://api.dingdong.work/sign-up";
        private const string DefaultModel = "gpt-6.1-sol";
        // Codex Desktop supports an explicit UI locale override. Keep the
        // installer output readable for Chinese users on first launch.
        private const string DefaultLocale = "zh-CN";
        private const string OfficialMsixUrl = "https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix";
        private const string StoreProductId = "9PLM9XGG6VKS";
        private const string KeyPlaceholder = "粘贴 sk- 开头的 API 密钥";

        private static readonly Color AccentColor = Color.FromArgb(24, 102, 220);
        private static readonly Color AccentDarkColor = Color.FromArgb(17, 72, 166);
        private static readonly Color TextColor = Color.FromArgb(20, 34, 58);
        private static readonly Color MutedTextColor = Color.FromArgb(91, 107, 132);
        private static readonly Color BorderColor = Color.FromArgb(211, 222, 237);
        private static readonly Color SurfaceColor = Color.White;
        private static readonly Color BackgroundColor = Color.FromArgb(243, 247, 252);
        private static readonly Color SuccessColor = Color.FromArgb(20, 148, 91);
        private static readonly Color WarningColor = Color.FromArgb(202, 126, 18);
        private static readonly Color ErrorColor = Color.FromArgb(206, 55, 69);

        private readonly Label statusLabel;
        private readonly Label statusDot;
        private readonly Label stageTitleLabel;
        private readonly Label stageDetailLabel;
        private readonly Label progressPercentLabel;
        private readonly Label progressMetaLabel;
        private readonly Label logStateLabel;
        private readonly TextBox keyBox;
        private readonly RichTextBox logBox;
        private readonly Button activateButton;
        private readonly Button openSiteButton;
        private readonly Button openCodexButton;
        private readonly Button offlineChineseButton;
        private readonly Button inspectChineseButton;
        private readonly Button desktopShortcutButton;
        private readonly Button computerUseButton;
        private readonly Button inspectStartupButton;
        private readonly Button repairStartupButton;
        private readonly Button inspectOrganizationButton;
        private readonly Button openCliButton;
        private readonly Button openInstallerFolderButton;
        private readonly Button openConfigFolderButton;
        private readonly Button copyLogButton;
        private readonly Button clearLogButton;
        private readonly CheckBox installIfMissingBox;
        private readonly CheckBox showKeyBox;
        private readonly FlatProgressBar progressBar;
        private readonly WorkflowStrip workflowStrip;
        private bool keyPlaceholderActive;
        private int lastDownloadLogPercent = -10;

        public MainForm()
        {
            Text = "DD Codex 安装助手";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 758);
            Size = new Size(920, 838);
            Font = new Font("Microsoft YaHei UI", 9.5F);
            BackColor = BackgroundColor;
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BackgroundColor,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = AccentDarkColor,
                Padding = new Padding(26, 18, 26, 14),
                Margin = new Padding(0)
            };
            root.Controls.Add(header, 0, 0);

            var title = new Label
            {
                Text = "DD Codex 安装助手",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 19F, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(24, 17)
            };
            header.Controls.Add(title);

            var versionBadge = new Label
            {
                Text = "v2.1.17",
                AutoSize = true,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = Color.FromArgb(191, 220, 255),
                Location = new Point(title.Right + 14, 29)
            };
            header.Controls.Add(versionBadge);

            var subtitle = new Label
            {
                Text = "检测环境、安装客户端并写入 DD API 配置，所有步骤都可实时查看。",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Color.FromArgb(218, 233, 252),
                Location = new Point(26, 61)
            };
            header.Controls.Add(subtitle);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(22, 18, 22, 14),
                BackColor = BackgroundColor,
                Margin = new Padding(0)
            };
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            root.Controls.Add(body, 0, 1);

            var statusPanel = CreateSurfacePanel(new Padding(14, 9, 14, 8), new Padding(0, 0, 0, 10));
            var statusLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = SurfaceColor,
                Margin = new Padding(0)
            };
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 26));
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusDot = new Label
            {
                Text = "●",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Symbol", 10F),
                ForeColor = AccentColor,
                Margin = new Padding(0)
            };
            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = TextColor,
                Text = "正在检测 Codex/ChatGPT 桌面版安装状态...",
                AutoEllipsis = true,
                Margin = new Padding(0)
            };
            statusLayout.Controls.Add(statusDot, 0, 0);
            statusLayout.Controls.Add(statusLabel, 1, 0);
            statusPanel.Controls.Add(statusLayout);
            body.Controls.Add(statusPanel, 0, 0);

            var keyPanel = CreateSurfacePanel(new Padding(14, 10, 14, 9), new Padding(0, 0, 0, 10));
            var keyLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 4,
                BackColor = SurfaceColor,
                Margin = new Padding(0)
            };
            keyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            keyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            keyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
            keyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            keyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            keyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            keyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var keyTitle = new Label
            {
                Text = "API 密钥",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = TextColor,
                Margin = new Padding(0)
            };
            keyLayout.Controls.Add(keyTitle, 0, 0);
            keyLayout.SetColumnSpan(keyTitle, 3);

            var keyHelpText =
                "KEY 获取方式：点击访问 https://api.dingdong.work/sign-up，注册后点击左侧 API 秘钥添加，复制到下方即可安装或者对接";
            var keyHelpLink = new LinkLabel
            {
                Text = keyHelpText,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ErrorColor,
                LinkColor = ErrorColor,
                ActiveLinkColor = AccentDarkColor,
                VisitedLinkColor = ErrorColor,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0)
            };
            var registerLinkStart = keyHelpText.IndexOf(RegisterUrl, StringComparison.Ordinal);
            if (registerLinkStart >= 0)
            {
                keyHelpLink.Links.Add(registerLinkStart, RegisterUrl.Length, RegisterUrl);
            }
            keyHelpLink.LinkClicked += (sender, args) => OpenUrl(args.Link.LinkData as string ?? RegisterUrl);
            keyLayout.Controls.Add(keyHelpLink, 0, 1);
            keyLayout.SetColumnSpan(keyHelpLink, 3);

            keyBox = new TextBox
            {
                Dock = DockStyle.Fill,
                UseSystemPasswordChar = false,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F),
                Margin = new Padding(0, 3, 10, 3)
            };
            SetKeyPlaceholder();
            keyBox.Enter += (sender, args) =>
            {
                if (!keyPlaceholderActive) return;
                keyPlaceholderActive = false;
                keyBox.Text = "";
                keyBox.ForeColor = TextColor;
                keyBox.UseSystemPasswordChar = !showKeyBox.Checked;
            };
            keyBox.Leave += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(keyBox.Text)) SetKeyPlaceholder();
            };
            keyLayout.Controls.Add(keyBox, 0, 2);

            showKeyBox = new CheckBox
            {
                Text = "显示",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = MutedTextColor,
                Margin = new Padding(0, 2, 8, 2)
            };
            showKeyBox.CheckedChanged += (sender, args) =>
            {
                if (!keyPlaceholderActive) keyBox.UseSystemPasswordChar = !showKeyBox.Checked;
            };
            keyLayout.Controls.Add(showKeyBox, 1, 2);

            activateButton = new Button
            {
                Text = "开始安装与配置",
                Dock = DockStyle.Fill,
                BackColor = AccentColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 1, 0, 1)
            };
            activateButton.FlatAppearance.BorderSize = 0;
            activateButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 86, 194);
            activateButton.Click += async (sender, args) => await ActivateAsync();
            keyLayout.Controls.Add(activateButton, 2, 2);

            installIfMissingBox = new CheckBox
            {
                Text = "未安装时自动下载并安装官方 Windows 客户端",
                Checked = true,
                Dock = DockStyle.Fill,
                ForeColor = MutedTextColor,
                Margin = new Padding(0, 4, 0, 0)
            };
            keyLayout.Controls.Add(installIfMissingBox, 0, 3);
            keyLayout.SetColumnSpan(installIfMissingBox, 3);
            keyPanel.Controls.Add(keyLayout);
            body.Controls.Add(keyPanel, 0, 1);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = BackgroundColor,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0, 2, 0, 2)
            };
            openSiteButton = CreateSecondaryButton("获取 API 密钥", 142);
            openSiteButton.Click += (sender, args) => OpenUrl(RegisterUrl);
            buttonPanel.Controls.Add(openSiteButton);
            openCodexButton = CreateSecondaryButton("打开 Codex", 118);
            openCodexButton.Click += (sender, args) => OpenCodex();
            buttonPanel.Controls.Add(openCodexButton);
            offlineChineseButton = CreateSecondaryButton("启用离线中文", 132);
            offlineChineseButton.Click += async (sender, args) => await EnableOfflineChineseAsync();
            var maintenanceMenu = new ContextMenuStrip();
            var disableMaintenance = new ToolStripMenuItem("关闭中文自动检查");
            disableMaintenance.Click += (sender, args) =>
            {
                try { LogInfo(ChineseMaintenanceSupport.DisableForCurrentUser()); }
                catch (Exception) { LogWarning("关闭中文自动检查未完成，请重试。"); }
            };
            maintenanceMenu.Items.Add(disableMaintenance);
            offlineChineseButton.ContextMenuStrip = maintenanceMenu;
            buttonPanel.Controls.Add(offlineChineseButton);
            desktopShortcutButton = CreateSecondaryButton("创建桌面快捷方式", 146);
            desktopShortcutButton.Click += async (sender, args) => await CreateDesktopShortcutAsync();
            buttonPanel.Controls.Add(desktopShortcutButton);
            inspectChineseButton = CreateSecondaryButton("检查中文状态", 132);
            inspectChineseButton.Click += async (sender, args) => await InspectChineseAsync();
            buttonPanel.Controls.Add(inspectChineseButton);
            computerUseButton = CreateSecondaryButton("安装 Computer Use", 174);
            computerUseButton.Margin = new Padding(0, 4, 10, 0);
            computerUseButton.Click += async (sender, args) => await InstallComputerUseAsync();
            buttonPanel.Controls.Add(computerUseButton);
            inspectStartupButton = CreateSecondaryButton("检查启动状态", 132);
            inspectStartupButton.Click += async (sender, args) => await InspectStartupAsync();
            buttonPanel.Controls.Add(inspectStartupButton);
            repairStartupButton = CreateSecondaryButton("修复启动注册", 132);
            repairStartupButton.Click += async (sender, args) => await RepairStartupAsync();
            buttonPanel.Controls.Add(repairStartupButton);
            inspectOrganizationButton = CreateSecondaryButton("检查组织设置", 132);
            inspectOrganizationButton.Click += async (sender, args) => await InspectOrganizationAsync();
            buttonPanel.Controls.Add(inspectOrganizationButton);
            openCliButton = CreateSecondaryButton("打开命令行版", 132);
            openCliButton.Click += async (sender, args) => await OpenCliAsync();
            buttonPanel.Controls.Add(openCliButton);
            openInstallerFolderButton = CreateSecondaryButton("打开Codex自动下载msi路径", 252);
            openInstallerFolderButton.Click += (sender, args) => OpenLocalFolder(@"%TEMP%\DDCodexInstaller\");
            buttonPanel.Controls.Add(openInstallerFolderButton);
            openConfigFolderButton = CreateSecondaryButton("打开Codex配置文件夹", 210);
            openConfigFolderButton.Click += (sender, args) => OpenLocalFolder(@"%USERPROFILE%\.codex\");
            buttonPanel.Controls.Add(openConfigFolderButton);
            body.Controls.Add(buttonPanel, 0, 2);

            workflowStrip = new WorkflowStrip
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 8),
                BackColor = BackgroundColor
            };
            body.Controls.Add(workflowStrip, 0, 3);

            var progressPanel = CreateSurfacePanel(new Padding(14, 10, 14, 9), new Padding(0, 0, 0, 10));
            var progressLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = SurfaceColor,
                Margin = new Padding(0)
            };
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progressLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            progressLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
            stageTitleLabel = new Label
            {
                Text = "等待开始",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = TextColor,
                Margin = new Padding(0)
            };
            progressPercentLabel = new Label
            {
                Text = "0%",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentColor,
                Margin = new Padding(0)
            };
            progressBar = new FlatProgressBar
            {
                Dock = DockStyle.Fill,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Margin = new Padding(0, 5, 0, 5)
            };
            stageDetailLabel = new Label
            {
                Text = "输入 API 密钥后即可开始。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MutedTextColor,
                AutoEllipsis = true,
                Margin = new Padding(0)
            };
            progressMetaLabel = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(118, 132, 151),
                AutoEllipsis = true,
                Margin = new Padding(0)
            };
            progressLayout.Controls.Add(stageTitleLabel, 0, 0);
            progressLayout.Controls.Add(progressPercentLabel, 1, 0);
            progressLayout.Controls.Add(progressBar, 0, 1);
            progressLayout.SetColumnSpan(progressBar, 2);
            progressLayout.Controls.Add(stageDetailLabel, 0, 2);
            progressLayout.SetColumnSpan(stageDetailLabel, 2);
            progressLayout.Controls.Add(progressMetaLabel, 0, 3);
            progressLayout.SetColumnSpan(progressMetaLabel, 2);
            progressPanel.Controls.Add(progressLayout);
            body.Controls.Add(progressPanel, 0, 4);

            var logPanel = CreateSurfacePanel(new Padding(0), new Padding(0, 0, 0, 7));
            var logLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = SurfaceColor,
                Margin = new Padding(0)
            };
            logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
            logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.FromArgb(249, 251, 254),
                Padding = new Padding(12, 5, 8, 5),
                Margin = new Padding(0)
            };
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            logHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            var logTitle = new Label
            {
                Text = "运行日志",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                ForeColor = TextColor,
                Margin = new Padding(0, 6, 12, 0)
            };
            logStateLabel = new Label
            {
                Text = "等待任务",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = MutedTextColor,
                Margin = new Padding(0)
            };
            copyLogButton = CreateLogButton("复制日志");
            copyLogButton.Click += (sender, args) => CopyLog();
            clearLogButton = CreateLogButton("清空");
            clearLogButton.Click += (sender, args) => ClearLog();
            logHeader.Controls.Add(logTitle, 0, 0);
            logHeader.Controls.Add(logStateLabel, 1, 0);
            logHeader.Controls.Add(copyLogButton, 2, 0);
            logHeader.Controls.Add(clearLogButton, 3, 0);
            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9F),
                DetectUrls = false,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Margin = new Padding(0),
                Padding = new Padding(12, 8, 12, 8)
            };
            logLayout.Controls.Add(logHeader, 0, 0);
            logLayout.Controls.Add(logBox, 0, 1);
            logPanel.Controls.Add(logLayout);
            body.Controls.Add(logPanel, 0, 5);

            var footer = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(118, 132, 151),
                Text = "配置生效提示：如果 Codex 正在运行，请在完成后退出并重新打开。",
                AutoEllipsis = true,
                Margin = new Padding(1, 2, 0, 0)
            };
            body.Controls.Add(footer, 0, 6);

            var toolTip = new ToolTip();
            toolTip.SetToolTip(copyLogButton, "复制全部运行日志");
            toolTip.SetToolTip(clearLogButton, "清空当前日志");
            toolTip.SetToolTip(showKeyBox, "切换 API 密钥可见性");
            toolTip.SetToolTip(inspectOrganizationButton, "排查“无法加载组织设置”：配置/日志检查后，以包身份测试官方后台的三步读取；结束后弹出可复制日志。无需输入 API 密钥。");
            toolTip.SetToolTip(openCliButton, "选择项目目录后，以 Codex 包身份打开官方命令行界面，沿用现有配置；供桌面界面不可用时临时使用。");

            Shown += async (sender, args) =>
            {
                activateButton.Focus();
                await DetectAsync();
            };
            LogInfo("助手已启动，正在准备环境检测。");
        }

        private static Panel CreateSurfacePanel(Padding padding, Padding margin)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = SurfaceColor,
                Padding = padding,
                Margin = margin
            };
            panel.Paint += (sender, args) =>
            {
                var rectangle = panel.ClientRectangle;
                rectangle.Width -= 1;
                rectangle.Height -= 1;
                using (var pen = new Pen(BorderColor))
                {
                    args.Graphics.DrawRectangle(pen, rectangle);
                }
            };
            return panel;
        }

        private static Button CreateSecondaryButton(string text, int width)
        {
            var button = new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                BackColor = Color.White,
                ForeColor = AccentColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0)
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(174, 197, 226);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 244, 255);
            return button;
        }

        private static Button CreateLogButton(string text)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                ForeColor = MutedTextColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                Margin = new Padding(4, 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 244, 252);
            return button;
        }

        private async Task DetectAsync()
        {
            SetProgress(3, "正在检测运行环境", "检查 Codex/ChatGPT 桌面版安装状态...", "正在查询 Windows 应用包", 0, -1, true);
            SetStatus("正在检测 Codex/ChatGPT 桌面版安装状态...", AccentColor);
            LogInfo("开始检测 Codex/ChatGPT 桌面版安装状态。");
            try
            {
                bool installed = await Task.Run(() => IsCodexInstalled());
                workflowStrip.SetState(0, 0, false);
                if (installed)
                {
                    SetStatus("已检测到 Codex/ChatGPT，配置时将跳过下载安装。", SuccessColor);
                    SetProgress(8, "环境检测完成", "已安装客户端，可直接验证密钥并写入配置。", "客户端状态：已安装", 0, 0, false);
                    LogSuccess("检测完成：已安装 Codex/ChatGPT 桌面版。");
                }
                else
                {
                    SetStatus("未检测到 Codex/ChatGPT，可在配置时自动下载安装。", WarningColor);
                    SetProgress(8, "环境检测完成", "尚未安装客户端，开始任务后将下载官方安装包。", "客户端状态：未安装", 0, 0, false);
                    LogWarning("检测完成：未安装 Codex/ChatGPT 桌面版。");
                }
            }
            catch (Exception ex)
            {
                workflowStrip.SetState(0, -1, true);
                SetStatus("环境检测未完成，请查看运行日志。", ErrorColor);
                SetProgress(0, "环境检测失败", ex.Message, "可继续尝试开始配置", 0, -1, false);
                LogError("环境检测失败：" + ex.Message);
            }
        }

        private async Task ActivateAsync()
        {
            string apiKey = GetApiKeyInput();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("请先粘贴 API 密钥。", "DD Codex 安装助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool installIfMissing = installIfMissingBox.Checked;
            SetBusy(true);
            LogSeparator();
            LogInfo("开始新的安装与配置任务。");
            SetStatus("任务执行中，请保持网络连接。", AccentColor);
            workflowStrip.SetState(1, 0, false);
            SetProgress(10, "正在验证 API 密钥", "连接 DD API 并读取可用模型...", "目标地址：" + ApiBaseUrl, 1, 0, true);
            try
            {
                int modelCount = await Task.Run(() => ValidateApiKey(apiKey));
                LogSuccess("API 密钥验证通过，可用模型数量：" + modelCount + "。");
                SetProgress(18, "API 密钥验证通过", "密钥有效，正在确认客户端安装状态。", "已读取 " + modelCount + " 个可用模型", 1, 1, false);

                bool installed = await Task.Run(() => IsCodexInstalled());
                if (installed)
                {
                    workflowStrip.SetState(2, 2, false);
                    SetProgress(82, "客户端已安装", "跳过下载和安装，准备写入 Codex 配置。", "安装步骤：已跳过", 2, 2, false);
                    LogSuccess("已检测到 Codex/ChatGPT，跳过下载和安装。");
                }
                else if (installIfMissing)
                {
                    await InstallCodexAsync();
                    installed = true;
                }
                else
                {
                    workflowStrip.SetState(2, 2, false);
                    SetProgress(82, "已跳过客户端安装", "未勾选自动安装，将仅写入 Codex 配置。", "稍后可从官方地址手动安装", 2, 2, false);
                    LogWarning("未安装 Codex/ChatGPT，且未勾选自动安装。");
                }

                bool desktopShortcutReady = false;
                if (installed)
                {
                    try
                    {
                        LogInfo("正在为 Codex 创建桌面快捷方式。");
                        LogSuccess(await Task.Run(() => DesktopShortcutSupport.CreateForCurrentUser()));
                        desktopShortcutReady = true;
                    }
                    catch (Exception shortcutError)
                    {
                        LogWarning("桌面快捷方式未完成：" + shortcutError.Message);
                        LogWarning("可单独点击“创建桌面快捷方式”重试，无需输入 API 密钥。");
                    }
                }

                workflowStrip.SetState(3, 2, false);
                SetProgress(91, "正在写入配置", "备份现有配置并写入 DD API 地址与凭据...", "%USERPROFILE%\\.codex", 3, 2, true);
                LogInfo("正在写入 Codex 配置和当前用户环境变量。");
                await Task.Run(() => WriteCodexConfig(apiKey));
                BroadcastEnvironmentChange();
                LogSuccess("Codex 配置、认证信息、中文语言选项和环境变量写入完成。");

                bool offlineChineseReady = false;
                bool skipOfflineChinese = false;
                while (await Task.Run(() => OfflineChineseSupport.IsDesktopRunning()))
                {
                    LogWarning("Codex 仍在后台运行；请完全退出后点击“重试”，继续中文配置。");
                    SetStatus("API 配置已写入，等待您完全退出 Codex 后继续中文配置。", WarningColor);
                    DialogResult choice = MessageBox.Show(
                        "Codex 仍在运行。请完全退出 Codex（包括后台/托盘进程），然后点击“重试”继续中文配置。\n\n点击“取消”将跳过中文步骤，稍后可单独点击“启用离线中文”。",
                        "等待 Codex 退出", MessageBoxButtons.RetryCancel, MessageBoxIcon.Information);
                    if (choice == DialogResult.Cancel) { skipOfflineChinese = true; break; }
                }
                if (!skipOfflineChinese)
                {
                    SetProgress(96, "正在启用离线中文", "初始化本地组件并备份语言缓存...", "此步骤无需访问外网", 3, 2, true);
                    bool configureMaintenance = false;
                    try
                    {
                        string result = await Task.Run(() => OfflineChineseSupport.Enable());
                        LogSuccess(result);
                        offlineChineseReady = true;
                        configureMaintenance = true;
                    }
                    catch (Exception languageError)
                    {
                        LogWarning("API 配置成功，离线中文未完成：" + languageError.Message);
                        var partial = languageError as OfflineChineseIncompleteException;
                        configureMaintenance = partial != null && partial.Completed > 0;
                        LogWarning("可在修复原因后单独点击“启用离线中文”重试。");
                    }
                    if (configureMaintenance) await ConfigureChineseMaintenanceAsync();
                }
                else
                {
                    LogWarning("请完全退出 Codex 后，回到助手点击“启用离线中文”。");
                }

                bool running = await Task.Run(() => IsCodexRunning());
                workflowStrip.SetState(3, 3, false);
                SetProgress(100, offlineChineseReady ? "配置写入完成，请打开验证" : "API 配置完成，中文尚未完成", running
                    ? "检测到 Codex 正在运行，请退出后重新打开。"
                    : "配置已写入，请打开 Codex 验证界面与连接。",
                    "DD API 地址：" + ApiBaseUrl, 3, 3, false);
                SetStatus(offlineChineseReady
                    ? "API 和中文缓存已写入，请打开 Codex 验证界面。"
                    : "API 配置完成；中文尚未完成，请查看日志并重试。", offlineChineseReady ? SuccessColor : WarningColor);
                if (offlineChineseReady) LogSuccess("API 配置和中文缓存已写入；界面效果需重新打开 Codex 验证。");
                else LogWarning("API 配置已完成；离线中文尚未启用，请查看上方日志并单独重试。");

                MessageBox.Show(
                    (running
                        ? "安装与配置已完成。\n\n检测到 Codex/ChatGPT 当前正在运行，请先退出，再重新打开即可使用 DD API 服务。"
                        : "安装与配置已完成。\n\n现在可以打开 Codex 使用 DD API 服务。") +
                    (offlineChineseReady ? "\n\n中文缓存已写入，请打开客户端验证。若仍英文，请回到助手点击“检查中文状态”，再“复制日志”。" : "\n\n离线中文尚未完成，请先查看日志，完全退出 Codex 后点击“启用离线中文”重试。") +
                    (desktopShortcutReady ? "\n\n桌面 Codex 快捷方式已就绪。" : "\n\n桌面快捷方式未创建，可点击“创建桌面快捷方式”重试。"),
                    "DD Codex 安装助手",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                workflowStrip.SetState(workflowStrip.CurrentStep, workflowStrip.CompletedStep, true);
                SetStatus("任务失败，请根据日志中的错误信息重试。", ErrorColor);
                SetProgress(progressBar.Value, "任务执行失败", ex.Message, "错误详情已写入运行日志", workflowStrip.CurrentStep, workflowStrip.CompletedStep, false);
                LogError("任务失败：" + ex.Message);
                MessageBox.Show(ex.Message, "激活失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task InstallCodexAsync()
        {
            workflowStrip.SetState(2, 1, false);
            LogInfo("未检测到 Codex/ChatGPT，准备下载安装官方客户端。");
            string dir = Path.Combine(Path.GetTempPath(), "DDCodexInstaller");
            Directory.CreateDirectory(dir);
            string msixPath = Path.Combine(dir, "ChatGPT-x64.msix");

            Exception primaryInstallError = null;
            try
            {
                if (!File.Exists(msixPath) || new FileInfo(msixPath).Length < 1024 * 1024)
                {
                    await DownloadFileAsync(OfficialMsixUrl, msixPath);
                }
                else
                {
                    long cachedSize = new FileInfo(msixPath).Length;
                    SetProgress(72, "安装包已准备", "发现已下载的官方安装包，正在复用。", "文件大小：" + FormatBytes(cachedSize), 2, 1, false);
                    LogSuccess("发现可复用的官方安装包：" + FormatBytes(cachedSize) + "。");
                }

                SetProgress(76, "正在安装官方客户端", "Windows 正在注册应用包，此阶段不提供精确百分比。", "安装方式：Add-AppxPackage", 2, 1, true);
                LogInfo("开始安装官方 MSIX 应用包。");
                await RunProcessAsync(
                    WindowsPowerShellPath(),
                    PowerShellArgs("Add-AppxPackage -ForceApplicationShutdown -Path " + PsQuote(msixPath)),
                    180000,
                    "MSIX");
                LogSuccess("官方 MSIX 应用包安装命令执行完成。");
            }
            catch (Exception first)
            {
                primaryInstallError = first;
            }

            if (primaryInstallError != null)
            {
                LogWarning("MSIX 安装未完成，切换到 winget 备用方案：" + primaryInstallError.Message);
                SetProgress(80, "正在使用备用安装方式", "winget 正在从 Microsoft Store 安装客户端。", "安装方式：winget / Microsoft Store", 2, 1, true);
                Exception fallbackInstallError = null;
                try
                {
                    await RunProcessAsync(
                        "winget",
                        "install --id " + StoreProductId + " -s msstore --accept-package-agreements --accept-source-agreements",
                        240000,
                        "winget");
                    LogSuccess("winget 安装命令执行完成。");
                }
                catch (Exception second)
                {
                    fallbackInstallError = second;
                }

                if (fallbackInstallError != null)
                {
                    LogError("两种自动安装方式均未成功，将打开官方下载地址。");
                    OpenUrl(OfficialMsixUrl);
                    throw new InvalidOperationException("自动安装 Codex/ChatGPT 未成功。请手动安装官方安装包后，再回到助手点击激活。\n\n详细信息：" + fallbackInstallError.Message);
                }
            }

            SetProgress(88, "正在确认安装结果", "重新扫描 Windows 应用包和命令入口。", "请稍候...", 2, 1, true);
            bool installed = await Task.Run(() => IsCodexInstalled());
            if (!installed)
            {
                throw new InvalidOperationException("安装命令已结束，但仍未检测到 Codex/ChatGPT。请重启 Windows 后重试，或使用官方下载地址手动安装。");
            }
            workflowStrip.SetState(2, 2, false);
            SetProgress(90, "客户端安装完成", "已检测到 Codex/ChatGPT，下一步将写入配置。", "安装状态：已确认", 2, 2, false);
            LogSuccess("安装结果确认完成：已检测到 Codex/ChatGPT。");
        }

        private async Task EnableOfflineChineseAsync()
        {
            SetBusy(true);
            LogInfo("开始启用离线简体中文（无需 API 密钥、无需外网）。");
            try
            {
                SetStatus("正在准备离线中文配置...", AccentColor);
                InstalledDesktopApp client = await Task.Run(() => DesktopShortcutSupport.FindInstalledApp());
                LogInfo("助手版本：2.1.17；Codex 客户端版本：" + client.Version + "。");
                string result = null;
                OfflineChineseIncompleteException partial = null;
                try { result = await Task.Run(() => OfflineChineseSupport.Enable()); }
                catch (OfflineChineseIncompleteException ex) { partial = ex; }
                if (partial != null)
                {
                    LogWarning("离线中文未完成：" + partial.Message);
                    if (partial.Completed > 0) await ConfigureChineseMaintenanceAsync();
                    SetStatus(partial.Completed > 0 ? "中文配置部分完成，请打开 Codex 验证，并查看未完成目录。" : "离线中文未完成，请查看日志并重试。", WarningColor);
                    MessageBox.Show(partial.Message, partial.Completed > 0 ? "中文配置部分完成" : "离线中文未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                LogSuccess(result);
                await ConfigureChineseMaintenanceAsync();
                SetStatus("中文缓存已写入，请重新打开 Codex 验证。", SuccessColor);
                MessageBox.Show(result + "\n\n请重新打开 Codex 验证中文界面；若仍英文，请回到助手点击“检查中文状态”，再“复制日志”。", "中文配置写入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogWarning("离线中文未完成：" + ex.Message);
                SetStatus("离线中文未完成，请查看日志并重试。", WarningColor);
                MessageBox.Show(ex.Message, "离线中文未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); }
        }

        private async Task CreateDesktopShortcutAsync()
        {
            SetBusy(true);
            LogInfo("开始创建 Codex 桌面快捷方式（无需 API 密钥）。");
            try
            {
                string result = await Task.Run(() => DesktopShortcutSupport.CreateForCurrentUser());
                LogSuccess(result);
                SetStatus("桌面快捷方式已就绪，可双击桌面的 Codex 打开客户端。", SuccessColor);
                MessageBox.Show(result, "桌面快捷方式", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogWarning("桌面快捷方式未完成：" + ex.Message);
                SetStatus("快捷方式未完成，请查看日志并重试。", WarningColor);
                MessageBox.Show(ex.Message, "快捷方式未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); }
        }

        private async Task ConfigureChineseMaintenanceAsync()
        {
            try { LogSuccess(await Task.Run(() => ChineseMaintenanceSupport.InstallForCurrentUser())); }
            catch (Exception)
            {
                LogWarning("本次中文缓存已写入，但中文自动检查未全部配置。请重新点击“启用离线中文”，并核对桌面入口与登录启动项。");
            }
        }

        private async Task InstallComputerUseAsync()
        {
            SetBusy(true);
            LogInfo("开始安装并启用官方 Computer Use 插件（使用客户本机已准备的插件库）。");
            try
            {
                SetStatus("正在检查并安装 Computer Use 插件...", AccentColor);
                InstalledDesktopApp client = await Task.Run(() => DesktopShortcutSupport.FindInstalledApp());
                LogInfo("助手版本：2.1.17；Codex 客户端版本：" + client.Version + "。");
                string result = await Task.Run(() => ComputerUseSupport.InstallForCurrentUser(client));
                LogSuccess(result);
                SetStatus("Computer Use 插件已启用，请重开 Codex 验证。", SuccessColor);
                MessageBox.Show(result, "Computer Use 插件已启用", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                LogWarning("Computer Use 插件未完成：" + ex.Message);
                SetStatus("Computer Use 插件未完成，请查看日志。", WarningColor);
                MessageBox.Show(ex.Message, "Computer Use 插件未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); }
        }

        private async Task InspectChineseAsync()
        {
            SetBusy(true);
            LogInfo("开始检查中文状态（不修改 Codex 配置，不输出密钥）。");
            try
            {
                LogInfo(await Task.Run(() => OfflineChineseSupport.Inspect()));
                SetStatus("中文诊断已写入下方日志，可点击“复制日志”。", AccentColor);
            }
            catch (Exception ex)
            {
                LogWarning("中文状态检查未完成：" + ex.Message);
                SetStatus("中文状态检查未完成，请查看日志。", WarningColor);
            }
            finally { SetBusy(false); }
        }

        private async Task InspectStartupAsync()
        {
            SetBusy(true);
            LogInfo("开始检查启动状态（只读，不输出凭据或原始系统事件）。");
            try
            {
                LogInfo(await Task.Run(() => DesktopRepairSupport.Inspect()));
                SetStatus("启动状态检查已完成，可复制日志。", AccentColor);
            }
            catch (Exception error) { LogWarning(error.Message); SetStatus("启动状态检查未完成，请复制日志。", WarningColor); }
            finally { SetBusy(false); }
        }

        private async Task RepairStartupAsync()
        {
            SetBusy(true);
            LogInfo("开始重新注册当前用户已安装的 Codex（保留配置、密钥与会话；请先完全退出客户端）。");
            try
            {
                LogSuccess(await Task.Run(() => DesktopRepairSupport.Repair()));
                SetStatus("应用注册回读通过，请打开 Codex 验证窗口。", SuccessColor);
                MessageBox.Show("当前用户的 Codex 应用注册已回读通过。\n\n请点击“打开 Codex”验证；若仍失败，点击“检查启动状态”，再“复制日志”。", "注册命令完成，启动待验证", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception error)
            {
                LogWarning(error.Message);
                SetStatus("启动注册修复未完成，请复制日志。", WarningColor);
                MessageBox.Show(error.Message, "启动注册修复未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { SetBusy(false); }
        }

        private async Task InspectOrganizationAsync()
        {
            SetBusy(true);
            LogInfo("开始检查组织设置：收集配置/日志，并以包身份启动独立官方后台测试三步读取。无需输入 API 密钥，不结束现有 Codex。");
            string report;
            try
            {
                OrganizationCheckResult result = await Task.Run(() => OrganizationCheckSupport.InspectWithProbe());
                report = result.Report;
                if (result.Complete) LogInfo(report); else LogWarning(report);
                SetStatus(result.Complete ? "组织设置检查已完成，可在弹窗复制日志。" : "组织设置部分项目未完成，请复制弹窗日志。", result.Complete ? AccentColor : WarningColor);
            }
            catch (Exception)
            {
                report = "DD Codex 安装助手 2.1.17 · 组织设置检查\n检查组件未能完成；请确认 Windows PowerShell 可用后重试。\n助手未改写 Codex 配置、密钥、中文缓存或组织策略。\n未输出原始异常内容。";
                LogWarning(report);
                SetStatus("组织设置检查未完成，请复制弹窗日志。", WarningColor);
            }
            finally { SetBusy(false); }
            using (var dialog = new DiagnosticReportForm(report)) dialog.ShowDialog(this);
        }

        private async Task OpenCliAsync()
        {
            string directory;
            using (var picker = new FolderBrowserDialog { Description = "选择要使用 Codex 的项目目录；命令行版沿用现有配置和权限要求。", ShowNewFolderButton = false })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                directory = picker.SelectedPath;
            }
            SetBusy(true);
            LogInfo("正在以 Codex 包身份打开官方命令行版，请在新窗口确认输入界面。");
            try
            {
                LogInfo(await Task.Run(() => OrganizationProbeSupport.OpenCli(directory)));
                SetStatus("命令行启动请求已提交，请查看新窗口；桌面问题仍待验证。", AccentColor);
            }
            catch (Exception)
            {
                LogWarning("命令行启动未完成，请确认当前用户已安装官方客户端并可使用包身份调试入口；未修改系统权限。");
                SetStatus("命令行入口未完成，请复制日志。", WarningColor);
            }
            finally { SetBusy(false); }
        }

        private static int ValidateApiKey(string apiKey)
        {
            var request = (HttpWebRequest)WebRequest.Create(ApiBaseUrl + "/models");
            request.Method = "GET";
            request.Timeout = 20000;
            request.Headers["Authorization"] = "Bearer " + apiKey;
            request.Accept = "application/json";
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                    {
                        throw new InvalidOperationException("API 密钥验证失败，HTTP " + (int)response.StatusCode);
                    }
                    using (var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null))
                    {
                        string body = reader.ReadToEnd();
                        return Regex.Matches(body, "\\\"id\\\"\\s*:").Count;
                    }
                }
            }
            catch (WebException ex)
            {
                string detail = "";
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream() ?? Stream.Null))
                    {
                        detail = reader.ReadToEnd();
                    }
                }
                if (detail.Length > 300) detail = detail.Substring(0, 300);
                throw new InvalidOperationException("API 密钥验证失败，请确认已在 api.dingdong.work 创建密钥。\n" + detail);
            }
        }

        private static bool IsCodexInstalled()
        {
            string output = RunPowerShellCapture(
                "Get-AppxPackage | Where-Object { $_.Name -match 'OpenAI|ChatGPT|Codex' -or $_.PackageFamilyName -match 'OpenAI|ChatGPT|Codex' } | Select-Object -First 1 -ExpandProperty Name",
                30000);
            if (!string.IsNullOrWhiteSpace(output)) return true;

            string localApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps");
            return File.Exists(Path.Combine(localApps, "codex.exe"))
                || File.Exists(Path.Combine(localApps, "ChatGPT.exe"));
        }

        private static bool IsCodexRunning()
        {
            return OfflineChineseSupport.IsDesktopRunning();
        }

        private static void WriteCodexConfig(string apiKey)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string codexDir = Path.Combine(home, ".codex");
            Directory.CreateDirectory(codexDir);

            string configPath = Path.Combine(codexDir, "config.toml");
            string authPath = Path.Combine(codexDir, "auth.json");
            BackupFile(configPath);
            BackupFile(authPath);

            string config = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : "";
            // Replace a previous provider's model (for example deepseek-flash) so
            // migrated Codex installations use a model available in the OPENAI group.
            config = UpsertTopLevelToml(config, "model", DefaultModel);
            config = UpsertTopLevelToml(config, "model_provider", "OpenAI");
            config = ConfigureDesktopLocale(config);
            config = UpsertModelProvider(config);
            File.WriteAllText(configPath, config, new UTF8Encoding(false));

            string authJson = UpsertAuthJson(authPath, apiKey);
            File.WriteAllText(authPath, authJson, new UTF8Encoding(false));

            Environment.SetEnvironmentVariable("OPENAI_API_KEY", apiKey, EnvironmentVariableTarget.User);
        }

        private static string UpsertAuthJson(string authPath, string apiKey)
        {
            string escapedKey = JsonEscape(apiKey);
            string line = "\"OPENAI_API_KEY\": \"" + escapedKey + "\"";
            if (!File.Exists(authPath))
            {
                return "{\n  " + line + "\n}\n";
            }

            string text = File.ReadAllText(authPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(text) || text.TrimStart()[0] != '{')
            {
                return "{\n  " + line + "\n}\n";
            }

            var regex = new Regex("\"OPENAI_API_KEY\"\\s*:\\s*\"(?:\\\\.|[^\"])*\"");
            if (regex.IsMatch(text))
            {
                return regex.Replace(text, line, 1);
            }

            int lastBrace = text.LastIndexOf('}');
            if (lastBrace < 0)
            {
                return "{\n  " + line + "\n}\n";
            }

            string before = text.Substring(0, lastBrace).TrimEnd();
            string after = text.Substring(lastBrace);
            string comma = before.EndsWith("{") ? "" : ",";
            return before + comma + Environment.NewLine + "  " + line + Environment.NewLine + after;
        }

        private static string UpsertTopLevelToml(string text, string key, string value)
        {
            string line = key + " = \"" + EscapeToml(value) + "\"";
            var regex = new Regex(@"(?m)^" + Regex.Escape(key) + @"\s*=.*$");
            if (regex.IsMatch(text)) return regex.Replace(text, line, 1);
            if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith("\n")) text += Environment.NewLine;
            return line + Environment.NewLine + text;
        }

        internal static string ConfigureDesktopLocale(string text)
        {
            // Desktop preferences belong to [desktop], not the CLI root.
            // Remove the misplaced entry written by older DD helper versions.
            int firstSection = FindNextTomlSection(text, 0);
            string top = text.Substring(0, firstSection);
            top = Regex.Replace(top, @"(?m)^[ \t]*(?:localeOverride|desktop\.localeOverride)[ \t]*=.*(?:\r?\n|$)", "");
            text = top + text.Substring(firstSection);
            var header = Regex.Match(text, @"(?m)^[ \t]*\[desktop\][ \t]*(?:#[^\r\n]*)?\r?$");
            string line = "localeOverride = \"" + DefaultLocale + "\"";
            if (!header.Success)
                return text.TrimEnd() + Environment.NewLine + Environment.NewLine + "[desktop]" + Environment.NewLine + line + Environment.NewLine;
            int next = FindNextTomlSection(text, header.Index + header.Length);
            string section = text.Substring(header.Index, next - header.Index);
            var locale = new Regex(@"(?m)^[ \t]*localeOverride[ \t]*=[^\r\n]*");
            if (locale.IsMatch(section)) section = locale.Replace(section, line);
            else section = section.TrimEnd() + Environment.NewLine + line + Environment.NewLine + Environment.NewLine;
            return text.Substring(0, header.Index) + section + text.Substring(next);
        }

        private static string UpsertModelProvider(string text)
        {
            var desired = new Dictionary<string, string>
            {
                {"name", "OpenAI"},
                {"base_url", ApiBaseUrl},
                {"wire_api", "responses"},
                {"requires_openai_auth", "true"}
            };

            string header = "[model_providers.OpenAI]";
            int start = text.IndexOf(header, StringComparison.Ordinal);
            if (start < 0)
            {
                if (!string.IsNullOrWhiteSpace(text) && !text.EndsWith("\n")) text += Environment.NewLine;
                text += Environment.NewLine + header + Environment.NewLine;
                text += "name = \"OpenAI\"" + Environment.NewLine;
                text += "base_url = \"" + ApiBaseUrl + "\"" + Environment.NewLine;
                text += "wire_api = \"responses\"" + Environment.NewLine;
                text += "requires_openai_auth = true" + Environment.NewLine;
                return text;
            }

            int next = FindNextTomlSection(text, start + header.Length);
            string before = text.Substring(0, start);
            string section = text.Substring(start, next - start);
            string after = text.Substring(next);

            foreach (var item in desired)
            {
                string tomlValue = item.Value == "true" ? "true" : "\"" + EscapeToml(item.Value) + "\"";
                string line = item.Key + " = " + tomlValue;
                var regex = new Regex(@"(?m)^" + Regex.Escape(item.Key) + @"\s*=.*$");
                if (regex.IsMatch(section))
                {
                    section = regex.Replace(section, line, 1);
                }
                else
                {
                    if (!section.EndsWith("\n")) section += Environment.NewLine;
                    section += line + Environment.NewLine;
                }
            }
            return before + section + after;
        }

        private static int FindNextTomlSection(string text, int from)
        {
            var match = Regex.Match(text.Substring(from), @"(?m)^\s*\[");
            if (!match.Success) return text.Length;
            return from + match.Index;
        }

        private static void BackupFile(string path)
        {
            if (!File.Exists(path)) return;
            string backup = path + ".ddbackup-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(path, backup, false);
        }

        private async Task DownloadFileAsync(string url, string destination)
        {
            string partialPath = destination + ".partial";
            TryDeleteFile(partialPath);
            lastDownloadLogPercent = -10;
            DateTime started = DateTime.UtcNow;
            SetProgress(20, "正在下载安装包", "正在连接 OpenAI 官方下载服务器...", "准备下载", 2, 1, false);
            LogInfo("开始下载官方安装包：" + url);

            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("User-Agent", "DDCodexInstallerHelper/2.1.17");
                    client.DownloadProgressChanged += (sender, args) =>
                    {
                        double elapsedSeconds = Math.Max((DateTime.UtcNow - started).TotalSeconds, 0.1);
                        double bytesPerSecond = args.BytesReceived / elapsedSeconds;
                        int overall = 20 + (int)Math.Round(args.ProgressPercentage * 0.52);
                        string total = args.TotalBytesToReceive > 0 ? FormatBytes(args.TotalBytesToReceive) : "未知大小";
                        string meta = FormatBytes(args.BytesReceived) + " / " + total
                            + "    " + FormatBytes((long)bytesPerSecond) + "/s";
                        SetProgress(overall, "正在下载安装包", "官方 Windows 安装包下载中：" + args.ProgressPercentage + "%", meta, 2, 1, false);

                        if (args.ProgressPercentage >= lastDownloadLogPercent + 10 || args.ProgressPercentage == 100)
                        {
                            lastDownloadLogPercent = args.ProgressPercentage;
                            LogDetail("下载进度 " + args.ProgressPercentage + "%  |  " + meta);
                        }
                    };
                    await client.DownloadFileTaskAsync(new Uri(url), partialPath);
                }

                long fileSize = new FileInfo(partialPath).Length;
                if (fileSize < 1024 * 1024)
                {
                    throw new InvalidOperationException("下载的安装包大小异常：" + FormatBytes(fileSize));
                }
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(partialPath, destination);
                SetProgress(72, "安装包下载完成", "已完成完整性基础检查，准备开始安装。", "文件大小：" + FormatBytes(fileSize), 2, 1, false);
                LogSuccess("官方安装包下载完成，文件大小：" + FormatBytes(fileSize) + "。");
            }
            catch
            {
                TryDeleteFile(partialPath);
                throw;
            }
        }

        private async Task RunProcessAsync(string fileName, string arguments, int timeoutMs, string source)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await Task.Run(() => RunHiddenCaptureDetailed(fileName, arguments, timeoutMs, line =>
            {
                string clean = SanitizeProcessLine(line);
                if (!string.IsNullOrWhiteSpace(clean)) LogDetail(source + " | " + clean);
            }));
            stopwatch.Stop();
            LogDetail(source + " 进程已结束，用时 " + FormatDuration(stopwatch.Elapsed) + "。");
        }

        private static string WindowsPowerShellPath()
        {
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(executable)) throw new InvalidOperationException("未找到系统 Windows PowerShell：" + executable + "。请检查 Windows 系统组件；修改 Codex 配置不能解决此项检测失败。");
            return executable;
        }

        private static string RunPowerShellCapture(string command, int timeoutMs)
        {
            return RunHiddenCapture(
                WindowsPowerShellPath(),
                PowerShellArgs(command),
                timeoutMs);
        }

        private static void RunHidden(string fileName, string arguments, int timeoutMs)
        {
            string output = RunHiddenCapture(fileName, arguments, timeoutMs);
            if (output.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Some commands print non-fatal warnings, so only process exit code is enforced in RunHiddenCapture.
            }
        }

        private static string RunHiddenCapture(string fileName, string arguments, int timeoutMs)
        {
            return RunHiddenCaptureDetailed(fileName, arguments, timeoutMs, null);
        }

        private static string RunHiddenCaptureDetailed(string fileName, string arguments, int timeoutMs, Action<string> onLine)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var process = Process.Start(psi))
            {
                if (process == null) throw new InvalidOperationException("无法启动命令：" + fileName);
                var output = new StringBuilder();
                var error = new StringBuilder();
                object outputLock = new object();
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data == null) return;
                    lock (outputLock) output.AppendLine(args.Data);
                    if (onLine != null) onLine(args.Data);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null) return;
                    lock (outputLock) error.AppendLine(args.Data);
                    if (onLine != null) onLine(args.Data);
                };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("命令执行超时：" + fileName);
                }
                process.WaitForExit();
                string outputText;
                string errorText;
                lock (outputLock)
                {
                    outputText = output.ToString();
                    errorText = error.ToString();
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException((errorText + Environment.NewLine + outputText).Trim());
                }
                return outputText.Trim();
            }
        }

        private static string SanitizeProcessLine(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string clean = Regex.Replace(value, @"\x1B\[[0-9;?]*[ -/]*[@-~]", "");
            return clean.Replace("\b", "").Trim();
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "未知";
            string[] units = { "B", "KB", "MB", "GB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes >= 1)
            {
                return ((int)duration.TotalMinutes) + " 分 " + duration.Seconds + " 秒";
            }
            return Math.Max(1, (int)Math.Round(duration.TotalSeconds)) + " 秒";
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A stale partial download does not affect the existing installer package.
            }
        }

        private void OpenCodex()
        {
            try
            {
                LogInfo(DesktopShortcutSupport.Open(LogWarning));
                return;
            }
            catch (Exception ex)
            {
                Log("自动打开 Codex 未成功：" + ex.Message);
                MessageBox.Show(ex.Message + "\n\n可先点击“检查启动状态”，再复制日志。", "打开 Codex 未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLocalFolder(string location)
        {
            string folder = Environment.ExpandEnvironmentVariables(location);
            try
            {
                if (!Directory.Exists(folder))
                {
                    string message = "文件夹尚不存在：" + folder + "\n请先完成相应的下载或配置步骤后再打开。";
                    LogWarning(message);
                    MessageBox.Show(this, message, "打开文件夹", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
                LogInfo("已请求打开文件夹：" + folder);
            }
            catch (Exception ex)
            {
                LogWarning("打开文件夹未完成：" + folder + "；" + ex.Message);
                MessageBox.Show(this, "无法打开文件夹：" + folder + "\n" + ex.Message, "打开文件夹未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static string PsQuote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static string PowerShellArgs(string command)
        {
            return "-NoProfile -NonInteractive -Command " + CmdQuote(command);
        }

        private static string CmdQuote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string EscapeToml(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string JsonEscape(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }

        private enum LogLevel
        {
            Info,
            Success,
            Warning,
            Error,
            Detail
        }

        private void Log(string message)
        {
            LogMessage(LogLevel.Info, message);
        }

        private void LogInfo(string message)
        {
            LogMessage(LogLevel.Info, message);
        }

        private void LogSuccess(string message)
        {
            LogMessage(LogLevel.Success, message);
        }

        private void LogWarning(string message)
        {
            LogMessage(LogLevel.Warning, message);
        }

        private void LogError(string message)
        {
            LogMessage(LogLevel.Error, message);
        }

        private void LogDetail(string message)
        {
            LogMessage(LogLevel.Detail, message);
        }

        private void LogMessage(LogLevel level, string message)
        {
            if (logBox.InvokeRequired)
            {
                logBox.Invoke(new Action<LogLevel, string>(LogMessage), level, message);
                return;
            }

            string label;
            Color levelColor;
            switch (level)
            {
                case LogLevel.Success:
                    label = " OK ";
                    levelColor = SuccessColor;
                    break;
                case LogLevel.Warning:
                    label = "WARN";
                    levelColor = WarningColor;
                    break;
                case LogLevel.Error:
                    label = "ERR ";
                    levelColor = ErrorColor;
                    break;
                case LogLevel.Detail:
                    label = " ...";
                    levelColor = Color.FromArgb(112, 128, 150);
                    break;
                default:
                    label = "INFO";
                    levelColor = AccentColor;
                    break;
            }

            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionColor = Color.FromArgb(133, 146, 164);
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] ");
            logBox.SelectionColor = levelColor;
            logBox.AppendText(label + "  ");
            logBox.SelectionColor = level == LogLevel.Detail ? Color.FromArgb(91, 107, 132) : TextColor;
            logBox.AppendText(message + Environment.NewLine);
            logBox.SelectionColor = TextColor;
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();

            if (level == LogLevel.Error) logStateLabel.Text = "出现错误";
            else if (level == LogLevel.Success) logStateLabel.Text = "运行正常";
            else if (level == LogLevel.Warning) logStateLabel.Text = "需要注意";
            else if (logStateLabel.Text == "等待任务") logStateLabel.Text = "正在记录";
        }

        private void LogSeparator()
        {
            Ui(() =>
            {
                logBox.SelectionStart = logBox.TextLength;
                logBox.SelectionColor = Color.FromArgb(218, 226, 237);
                logBox.AppendText(new string('─', 68) + Environment.NewLine);
                logBox.SelectionColor = TextColor;
            });
        }

        private void CopyLog()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(logBox.Text)) return;
                Clipboard.SetText(logBox.Text);
                LogSuccess("运行日志已复制到剪贴板。");
            }
            catch (Exception ex)
            {
                LogError("复制日志失败：" + ex.Message);
            }
        }

        private void ClearLog()
        {
            logBox.Clear();
            logStateLabel.Text = "等待任务";
            LogInfo("日志已清空。");
        }

        private void SetStatus(string message, Color color)
        {
            Ui(() =>
            {
                statusLabel.Text = message;
                statusDot.ForeColor = color;
            });
        }

        private void SetProgress(
            int value,
            string title,
            string detail,
            string meta,
            int currentStep,
            int completedStep,
            bool marquee)
        {
            Ui(() =>
            {
                int safeValue = Math.Max(progressBar.Minimum, Math.Min(progressBar.Maximum, value));
                progressBar.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
                progressBar.MarqueeAnimationSpeed = marquee ? 28 : 0;
                if (!marquee) progressBar.Value = safeValue;
                progressPercentLabel.Text = marquee ? "处理中" : safeValue + "%";
                stageTitleLabel.Text = title;
                stageDetailLabel.Text = detail;
                progressMetaLabel.Text = meta;
                workflowStrip.SetState(currentStep, completedStep, false);
            });
        }

        private void SetBusy(bool busy)
        {
            Ui(() =>
            {
                activateButton.Enabled = !busy;
                openSiteButton.Enabled = !busy;
                openCodexButton.Enabled = !busy;
                offlineChineseButton.Enabled = !busy;
                inspectChineseButton.Enabled = !busy;
                desktopShortcutButton.Enabled = !busy;
                computerUseButton.Enabled = !busy;
                inspectStartupButton.Enabled = !busy;
                repairStartupButton.Enabled = !busy;
                inspectOrganizationButton.Enabled = !busy;
                openCliButton.Enabled = !busy;
                keyBox.Enabled = !busy;
                installIfMissingBox.Enabled = !busy;
                showKeyBox.Enabled = !busy;
                activateButton.Text = busy ? "正在处理..." : "开始安装与配置";
                Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
            });
        }

        private void SetKeyPlaceholder()
        {
            keyPlaceholderActive = true;
            keyBox.UseSystemPasswordChar = false;
            keyBox.Text = KeyPlaceholder;
            keyBox.ForeColor = Color.FromArgb(142, 154, 170);
            if (showKeyBox != null) showKeyBox.Checked = false;
        }

        private string GetApiKeyInput()
        {
            if (keyPlaceholderActive) return "";
            return keyBox.Text.Trim();
        }

        private void Ui(Action action)
        {
            if (InvokeRequired) Invoke(action);
            else action();
        }

        private sealed class FlatProgressBar : Control
        {
            private readonly System.Windows.Forms.Timer animationTimer;
            private int minimum;
            private int maximum = 100;
            private int currentValue;
            private int marqueeOffset;
            private ProgressBarStyle style = ProgressBarStyle.Continuous;

            public FlatProgressBar()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                Height = 18;
                animationTimer = new System.Windows.Forms.Timer { Interval = 28 };
                animationTimer.Tick += (sender, args) =>
                {
                    marqueeOffset += 8;
                    if (marqueeOffset > ClientSize.Width) marqueeOffset = -Math.Max(36, ClientSize.Width / 4);
                    Invalidate();
                };
            }

            public int Minimum
            {
                get { return minimum; }
                set { minimum = value; Invalidate(); }
            }

            public int Maximum
            {
                get { return maximum; }
                set { maximum = Math.Max(value, minimum + 1); Invalidate(); }
            }

            public int Value
            {
                get { return currentValue; }
                set { currentValue = Math.Max(minimum, Math.Min(maximum, value)); Invalidate(); }
            }

            public ProgressBarStyle Style
            {
                get { return style; }
                set
                {
                    style = value;
                    if (style == ProgressBarStyle.Marquee) animationTimer.Start();
                    else animationTimer.Stop();
                    Invalidate();
                }
            }

            public int MarqueeAnimationSpeed
            {
                get { return animationTimer.Interval; }
                set
                {
                    if (value <= 0) animationTimer.Stop();
                    else animationTimer.Interval = Math.Max(15, value);
                }
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);
                args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var track = new Rectangle(0, 2, Math.Max(1, ClientSize.Width - 1), Math.Max(5, ClientSize.Height - 5));
                using (var trackBrush = new SolidBrush(Color.FromArgb(229, 236, 246)))
                using (var borderPen = new Pen(Color.FromArgb(201, 214, 232)))
                {
                    args.Graphics.FillRectangle(trackBrush, track);
                    args.Graphics.DrawRectangle(borderPen, track);
                }

                Rectangle fill;
                if (style == ProgressBarStyle.Marquee)
                {
                    int width = Math.Max(36, ClientSize.Width / 4);
                    fill = new Rectangle(marqueeOffset, track.Top + 1, width, Math.Max(1, track.Height - 1));
                }
                else
                {
                    double ratio = (currentValue - minimum) / (double)Math.Max(1, maximum - minimum);
                    fill = new Rectangle(track.Left + 1, track.Top + 1, (int)Math.Round((track.Width - 1) * ratio), Math.Max(1, track.Height - 1));
                }

                if (fill.Width > 0)
                {
                    args.Graphics.SetClip(track);
                    using (var fillBrush = new SolidBrush(AccentColor))
                    {
                        args.Graphics.FillRectangle(fillBrush, fill);
                    }
                    args.Graphics.ResetClip();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) animationTimer.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class WorkflowStrip : Control
        {
            private readonly string[] steps = { "检测环境", "验证密钥", "安装客户端", "写入配置" };
            private bool failed;

            public int CurrentStep { get; private set; }
            public int CompletedStep { get; private set; }

            public WorkflowStrip()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
                CurrentStep = 0;
                CompletedStep = -1;
                Height = 52;
                MinimumSize = new Size(400, 52);
            }

            public void SetState(int currentStep, int completedStep, bool isFailed)
            {
                CurrentStep = Math.Max(0, Math.Min(steps.Length - 1, currentStep));
                CompletedStep = Math.Max(-1, Math.Min(steps.Length - 1, completedStep));
                failed = isFailed;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                base.OnPaint(args);
                args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                int left = 46;
                int right = Math.Max(left + 1, ClientSize.Width - 46);
                int y = 15;
                int spacing = (right - left) / (steps.Length - 1);

                using (var pendingPen = new Pen(Color.FromArgb(205, 216, 230), 2F))
                using (var completePen = new Pen(SuccessColor, 2F))
                {
                    for (int i = 0; i < steps.Length - 1; i++)
                    {
                        int x1 = left + i * spacing;
                        int x2 = left + (i + 1) * spacing;
                        args.Graphics.DrawLine(i < CompletedStep ? completePen : pendingPen, x1 + 11, y, x2 - 11, y);
                    }
                }

                for (int i = 0; i < steps.Length; i++)
                {
                    int x = left + i * spacing;
                    bool complete = i <= CompletedStep;
                    bool active = i == CurrentStep && !complete;
                    Color fill = complete ? SuccessColor : active ? AccentColor : Color.White;
                    Color border = failed && i == CurrentStep ? ErrorColor : complete ? SuccessColor : active ? AccentColor : Color.FromArgb(184, 199, 218);
                    if (failed && i == CurrentStep) fill = ErrorColor;

                    using (var brush = new SolidBrush(fill))
                    using (var pen = new Pen(border, 2F))
                    {
                        args.Graphics.FillEllipse(brush, x - 10, y - 10, 20, 20);
                        args.Graphics.DrawEllipse(pen, x - 10, y - 10, 20, 20);
                    }

                    string marker = complete ? "✓" : (i + 1).ToString();
                    Color markerColor = complete || active || (failed && i == CurrentStep) ? Color.White : Color.FromArgb(116, 132, 153);
                    TextRenderer.DrawText(
                        args.Graphics,
                        marker,
                        new Font("Segoe UI", 8F, FontStyle.Bold),
                        new Rectangle(x - 10, y - 9, 20, 18),
                        markerColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    TextRenderer.DrawText(
                        args.Graphics,
                        steps[i],
                        new Font("Microsoft YaHei UI", 8.5F, i == CurrentStep ? FontStyle.Bold : FontStyle.Regular),
                        new Rectangle(x - spacing / 2, 31, spacing, 22),
                        i == CurrentStep ? TextColor : MutedTextColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            UIntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        private static void BroadcastEnvironmentChange()
        {
            try
            {
                UIntPtr result;
                SendMessageTimeout(
                    new IntPtr(0xffff),
                    0x001A,
                    UIntPtr.Zero,
                    "Environment",
                    0x0002,
                    5000,
                    out result);
            }
            catch
            {
                // Best-effort only.
            }
        }
    }
}
