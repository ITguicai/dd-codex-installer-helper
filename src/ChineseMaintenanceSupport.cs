using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DDCodexInstallerHelper
{
    internal sealed class ChineseMaintenanceSettings
    {
        public int Version { get; set; }
        public string ConfigPath { get; set; }
        public string UserDataPath { get; set; }
    }

    internal static class ChineseMaintenanceSupport
    {
        private const string RunName = "DDCodexChineseMaintenance";
        private const string LaunchArgument = "--launch-codex-chinese";
        private const string RefreshArgument = "--refresh-chinese";
        private static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDCodexInstallerHelper", "chinese-maintenance"); } }
        private static string StatePath { get { return Path.Combine(Root, "settings.json"); } }
        private static string Launcher { get { return Path.Combine(Root, "DDCodexLauncher.exe"); } }

        internal static string InstallForCurrentUser()
        {
            string launcher = InstallAt(Root, Assembly.GetExecutingAssembly().Location, DesktopProfileSupport.ConfigPathForCurrentUser(), Environment.GetEnvironmentVariable("CODEX_ELECTRON_USER_DATA_PATH"));
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                RegisterStartupAt(key, launcher);
            InstalledDesktopApp app = DesktopShortcutSupport.FindInstalledApp();
            string shortcut = DesktopShortcutSupport.CreateWithLauncherAt(app, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDCodexInstallerHelper", "icons"), launcher, LaunchArgument);
            return "中文自动检查已配置：Windows 登录后检查一次，桌面 Codex 启动前检查一次。\n桌面启动入口：" + shortcut + "\n自动检查日志：" + Path.Combine(Root, "maintenance.log");
        }

        internal static string InstallAt(string root, string executable, string configPath, string userDataPath)
        {
            string launcher = InstallLauncherAt(root, executable);
            var settings = new ChineseMaintenanceSettings { Version = 1, ConfigPath = Path.GetFullPath(configPath), UserDataPath = userDataPath };
            File.WriteAllText(Path.Combine(root, "settings.json"), new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            return launcher;
        }

        internal static string EnsureLauncherForCurrentUser()
        {
            return InstallLauncherAt(Root, Assembly.GetExecutingAssembly().Location);
        }

        internal static string InstallLauncherAt(string root, string executable)
        {
            Directory.CreateDirectory(root);
            string launcher = Path.Combine(root, "DDCodexLauncher.exe");
            if (!String.Equals(Path.GetFullPath(executable), Path.GetFullPath(launcher), StringComparison.OrdinalIgnoreCase) &&
                !PublicFileSupport.SameContents(executable, launcher)) PublicFileSupport.Copy(executable, launcher, true);
            return launcher;
        }

        internal static string ExistingLauncherForCurrentUser()
        {
            return File.Exists(Launcher) && File.Exists(StatePath) ? Launcher : null;
        }

        internal static string LauncherArguments { get { return LaunchArgument; } }

        internal static void RegisterStartupAt(RegistryKey key, string launcher)
        {
            key.SetValue(RunName, Quote(launcher) + " " + RefreshArgument, RegistryValueKind.String);
        }

        internal static void RemoveStartupAt(RegistryKey key)
        {
            if (key != null) key.DeleteValue(RunName, false);
        }

        internal static bool HandleCommand(string[] arguments)
        {
            if (arguments.Length != 1 || (arguments[0] != LaunchArgument && arguments[0] != RefreshArgument)) return false;
            bool launch = arguments[0] == LaunchArgument;
            bool opened = false;
            bool startingCodex = false;
            try
            {
                using (var gate = new System.Threading.Mutex(false, @"Local\DDCodexChineseMaintenance-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value))
                {
                    bool entered;
                    try { entered = gate.WaitOne(launch ? 30000 : 0); } catch (System.Threading.AbandonedMutexException) { entered = true; }
                    if (!entered)
                    {
                        if (launch) throw new InvalidOperationException("中文自动检查组件正在执行。");
                        return true;
                    }
                    try
                    {
                        if (!File.Exists(StatePath))
                        {
                            if (launch) { startingCodex = true; DesktopShortcutSupport.OpenDirect(); opened = true; }
                            return true;
                        }
                        var settings = LoadSettings(Root);
                        InstalledDesktopApp app = DesktopShortcutSupport.FindInstalledApp();
                        bool running = OfflineChineseSupport.IsDesktopRunning();
                        string result = RepairAt(Path.Combine(app.InstallLocation, "app"), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), settings, running);
                        WriteLog(result);
                        if (launch) { startingCodex = true; DesktopShortcutSupport.OpenDirect(); opened = true; }
                    }
                    finally { gate.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                if (startingCodex)
                {
                    MessageBox.Show(error.Message, "打开 Codex 未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                WriteLog("中文自动检查未完成；未强制结束 Codex。请在助手里点击“检查中文状态”或“启用离线中文”。");
                if (launch)
                {
                    MessageBox.Show("中文自动检查未完成。可以先打开 Codex；如果仍英文，完全退出后在助手里重新启用离线中文。", "Codex 中文自动检查", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            if (launch && !opened)
            {
                try { DesktopShortcutSupport.OpenDirect(); }
                catch (Exception error) { MessageBox.Show(error.Message, "打开 Codex 未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            return true;
        }

        internal static string RepairAt(string appRoot, string roamingRoot, string localRoot, ChineseMaintenanceSettings settings, bool desktopRunning)
        {
            if (desktopRunning) return "Codex 正在运行，跳过缓存写入；下次从桌面 Codex 启动时再检查。";
            if (settings == null || settings.Version != 1 || String.IsNullOrWhiteSpace(settings.ConfigPath) || !Path.IsPathRooted(settings.ConfigPath))
                throw new InvalidOperationException("中文自动检查配置无法识别。");
            string config = File.Exists(settings.ConfigPath) ? File.ReadAllText(settings.ConfigPath, Encoding.UTF8) : "";
            Match desktop = Regex.Match(config, @"(?ms)^\s*\[desktop\][^\r\n]*\r?\n(?:(?!^\s*\[).)*");
            if (!Regex.IsMatch(desktop.Value, @"(?m)^\s*localeOverride\s*=\s*[""']zh-CN[""']\s*(?:#.*)?$"))
                return "当前未选择简体中文，跳过自动维护，保留用户语言选择。";
            if (Regex.IsMatch(config, @"(?ms)^\s*\[analytics\][^\r\n]*\r?\n(?:(?!^\s*\[).)*?^\s*enabled\s*=\s*false\s*(?:#.*)?$"))
                return "分析存储已关闭，跳过缓存维护，保留用户隐私设置。";
            var targets = DesktopProfileSupport.WriteTargets(DesktopProfileSupport.ResolveAt(appRoot, roamingRoot, localRoot, settings.UserDataPath));
            int repaired = 0;
            int completed = 0;
            var failures = new StringBuilder();
            foreach (DesktopProfile profile in targets)
            {
                try
                {
                    if (OfflineChineseSupport.EnsureAt(appRoot, roamingRoot, profile.UserDataDirectory)) repaired++;
                    completed++;
                }
                catch (Exception ex)
                {
                    failures.AppendLine("未完成目录：" + profile.CacheDirectory + "。" + Environment.NewLine + ex.Message);
                }
            }
            if (completed != targets.Count)
                return "中文自动检查部分完成：检查 " + targets.Count + " 个目录，完成 " + completed + " 个目录，补齐 " + repaired + " 个目录，未完成 " + (targets.Count - completed) + " 个目录；需在客户端验证界面。" + Environment.NewLine + failures.ToString().Trim();
            return "中文自动检查完成：检查 " + targets.Count + " 个目录，补齐 " + repaired + " 个目录；需在客户端验证界面。";
        }

        private static ChineseMaintenanceSettings LoadSettings(string root)
        {
            return new JavaScriptSerializer().Deserialize<ChineseMaintenanceSettings>(File.ReadAllText(Path.Combine(root, "settings.json"), Encoding.UTF8));
        }

        internal static string InspectForCurrentUser()
        {
            string launcher = ExistingLauncherForCurrentUser();
            string run = null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                if (key != null) run = key.GetValue(RunName) as string;
            bool registered = launcher != null && String.Equals(run, Quote(launcher) + " " + RefreshArgument, StringComparison.Ordinal);
            var report = new StringBuilder();
            report.AppendLine("中文自动检查启动组件：" + (launcher != null ? "已配置" : "未配置"));
            report.AppendLine("Windows 登录后检查登记：" + (registered ? "已登记（Windows 启动项仍可能被用户停用）" : "未登记"));
            report.AppendLine("自动检查日志：" + Path.Combine(Root, "maintenance.log"));
            string log = Path.Combine(Root, "maintenance.log");
            if (File.Exists(log))
            {
                report.AppendLine("最近自动检查记录：" + File.GetLastWriteTime(log).ToString("yyyy-MM-dd HH:mm:ss"));
                if (new FileInfo(log).Length <= 1048576)
                {
                    string[] lines = File.ReadAllLines(log, Encoding.UTF8);
                    for (int i = Math.Max(0, lines.Length - 5); i < lines.Length; i++)
                    {
                        // Only our fixed status messages belong in copied diagnostics.
                        if (Regex.IsMatch(lines[i], @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} (?:中文自动检查完成：检查 \d+ 个目录，补齐 \d+ 个目录；需在客户端验证界面。|中文自动检查部分完成：检查 \d+ 个目录，完成 \d+ 个目录，补齐 \d+ 个目录，未完成 \d+ 个目录；需在客户端验证界面。|Codex 正在运行，跳过缓存写入；下次从桌面 Codex 启动时再检查。|当前未选择简体中文，跳过自动维护，保留用户语言选择。|分析存储已关闭，跳过缓存维护，保留用户隐私设置。|中文自动检查未完成；未强制结束 Codex。请在助手里点击“检查中文状态”或“启用离线中文”。)$"))
                            report.AppendLine("自动检查：" + lines[i]);
                        if (Regex.IsMatch(lines[i], @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} (?:系统应用激活已接受；客户端进程：\d+。|系统应用激活未完成（HRESULT 0x[0-9A-F]{8}(?:（Win32 \d+）)?）；已通过桌面程序入口启动，进程：\d+。|Codex 未能启动。系统激活 HRESULT 0x[0-9A-F]{8}(?:（Win32 \d+）)?；桌面程序入口 HRESULT 0x[0-9A-F]{8}(?:（Win32 \d+）)?。请在助手里点击“检查启动状态”，再“复制日志”；注册异常可点击“修复启动注册”。)$"))
                            report.AppendLine("客户端启动：" + lines[i]);
                    }
                }
            }
            report.Append("桌面 Codex 启动前可自动补齐；开始菜单或客户端自动启动不会经过桌面启动前检查。");
            return report.ToString();
        }

        internal static void RecordLaunch(string message) { WriteLog(message); }

        internal static string DisableForCurrentUser()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                RemoveStartupAt(key);
            if (File.Exists(StatePath)) File.Delete(StatePath);
            // Keep the copied executable: existing desktop links can still
            // open Codex, even though there is no longer an auto-repair state.
            return "中文自动检查已关闭；已移除登录后检查登记。当前语言设置和中文缓存保留。";
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Root);
                string log = Path.Combine(Root, "maintenance.log");
                if (File.Exists(log) && new FileInfo(log).Length > 262144) File.Copy(log, log + ".previous", true);
                if (File.Exists(log) && new FileInfo(log).Length > 262144) File.WriteAllText(log, "", new UTF8Encoding(false));
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }

        private static string Quote(string value) { return "\"" + value + "\""; }
    }
}
