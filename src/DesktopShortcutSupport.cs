using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal sealed class InstalledDesktopApp
    {
        internal string AppUserModelId;
        internal string InstallLocation;
        internal string ExecutablePath;
        internal string Version;
    }

    internal static class DesktopShortcutSupport
    {
        private const string Description = "Codex 桌面客户端（DD Codex 安装助手创建）";

        internal static InstalledDesktopApp FindInstalledApp()
        {
            // Read the registered application ID rather than assuming an
            // executable alias, which can also point to the command-line CLI.
            string script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$p = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1
if (!$p) { throw '未找到当前用户安装的官方 Codex 桌面客户端。' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $p.InstallLocation 'AppxManifest.xml') -Raw
$app = $manifest.SelectNodes(""//*[local-name()='Applications']/*[local-name()='Application']"") | Where-Object { $_.Executable -match '(^|[\\/])(ChatGPT|Codex)\.exe$' } | Select-Object -First 1
if (!$app) { throw '客户端的桌面启动入口尚未适配。' }
[pscustomobject]@{ appId = ($p.PackageFamilyName + '!' + $app.Id); installLocation = $p.InstallLocation; executable = $app.Executable; version = $p.Version.ToString() } | ConvertTo-Json -Compress
";
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (Process process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("查询 Codex 桌面启动入口超时，请重试。");
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("查询 Codex 启动入口失败：" + stderr.Trim());
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(stdout.Trim());
                if (data == null || !data.ContainsKey("appId") || !Regex.IsMatch(data["appId"], @"^OpenAI\.Codex_[A-Za-z0-9]+![A-Za-z0-9.]+$"))
                    throw new InvalidOperationException("未读取到有效的 Codex 桌面启动入口。");
                string root = data["installLocation"];
                string executable = Path.GetFullPath(Path.Combine(root, data["executable"].Replace('/', Path.DirectorySeparatorChar)));
                if (!executable.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
                    throw new InvalidOperationException("Codex 桌面启动文件不存在，请修复客户端安装。");
                return new InstalledDesktopApp { AppUserModelId = data["appId"], InstallLocation = root, ExecutablePath = executable, Version = data["version"] };
            }
        }

        internal static string CreateForCurrentUser()
        {
            InstalledDesktopApp app = FindInstalledApp();
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDCodexInstallerHelper", "icons");
            string launcher = ChineseMaintenanceSupport.EnsureLauncherForCurrentUser();
            string shortcut = CreateWithLauncherAt(app, desktop, cache, launcher, ChineseMaintenanceSupport.LauncherArguments);
            return "Codex " + app.Version + "；桌面快捷方式已就绪：" + shortcut + "\n已使用系统应用激活启动组件；请双击验证窗口，创建成功不代表启动已验收。";
        }

        // Verification calls this with an isolated desktop and icon directory.
        internal static string CreateAt(InstalledDesktopApp app, string desktop, string iconDirectory)
        {
            return CreateWithLauncherAt(app, desktop, iconDirectory, null, null);
        }

        internal static string CreateWithLauncherAt(InstalledDesktopApp app, string desktop, string iconDirectory, string launcher, string launchArguments)
        {
            if (String.IsNullOrWhiteSpace(desktop)) throw new InvalidOperationException("未找到当前用户的桌面目录。");
            Directory.CreateDirectory(desktop);
            Directory.CreateDirectory(iconDirectory);
            string iconPath = Path.Combine(iconDirectory, "codex.ico");
            string sourceIcon = Path.Combine(app.InstallLocation, "app", "resources", "icon-chatgpt.ico");
            if (File.Exists(sourceIcon)) PublicFileSupport.Copy(sourceIcon, iconPath, true);
            else
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(app.ExecutablePath))
                {
                    if (icon == null) throw new InvalidOperationException("无法读取 Codex 图标。");
                    using (FileStream stream = File.Create(iconPath)) icon.Save(stream);
                }
            }

            object shellObject = null;
            object linkObject = null;
            try
            {
                shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                dynamic shell = shellObject;
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                string directArguments = "shell:AppsFolder\\" + app.AppUserModelId;
                string targetPath = launcher ?? explorer;
                string arguments = launcher == null ? directArguments : launchArguments;
                string shortcutPath = null;
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    string name = attempt == 0 ? "Codex.lnk" : attempt == 1 ? "Codex (DD安装助手).lnk" : "Codex (DD安装助手 " + attempt + ").lnk";
                    string candidate = Path.Combine(desktop, name);
                    if (!File.Exists(candidate)) { shortcutPath = candidate; break; }
                    linkObject = shell.CreateShortcut(candidate);
                    dynamic existing = linkObject;
                    bool sameTarget = String.Equals((string)existing.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase) && String.Equals((string)existing.Arguments, arguments, StringComparison.Ordinal);
                    bool ownedDirectShortcut = launcher != null && String.Equals((string)existing.TargetPath, explorer, StringComparison.OrdinalIgnoreCase) &&
                        String.Equals((string)existing.Arguments, directArguments, StringComparison.Ordinal) && String.Equals((string)existing.Description, Description, StringComparison.Ordinal);
                    if (ownedDirectShortcut)
                    {
                        existing.TargetPath = targetPath;
                        existing.Arguments = arguments;
                        existing.IconLocation = iconPath + ",0";
                        existing.Save();
                        sameTarget = true;
                    }
                    Marshal.FinalReleaseComObject(linkObject);
                    linkObject = null;
                    if (sameTarget) return candidate;
                }
                if (shortcutPath == null) throw new InvalidOperationException("桌面上存在多个同名快捷方式，请清理同名文件后重试。");
                linkObject = shell.CreateShortcut(shortcutPath);
                dynamic link = linkObject;
                link.TargetPath = targetPath;
                link.Arguments = arguments;
                link.Description = Description;
                link.IconLocation = iconPath + ",0";
                link.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                link.Save();
                if (!File.Exists(shortcutPath)) throw new InvalidOperationException("快捷方式未保存到桌面。");
                return shortcutPath;
            }
            finally
            {
                if (linkObject != null && Marshal.IsComObject(linkObject)) Marshal.FinalReleaseComObject(linkObject);
                if (shellObject != null && Marshal.IsComObject(shellObject)) Marshal.FinalReleaseComObject(shellObject);
            }
        }

        internal static string Open(Action<string> onWarning)
        {
            return OpenWithFallback(
                ChineseMaintenanceSupport.ExistingLauncherForCurrentUser,
                ChineseMaintenanceSupport.EnsureLauncherForCurrentUser,
                launcher => {
                    using (Process process = Process.Start(new ProcessStartInfo { FileName = launcher, Arguments = ChineseMaintenanceSupport.LauncherArguments, UseShellExecute = true }))
                        if (process == null) throw new InvalidOperationException("启动组件未返回进程。");
                }, OpenDirect, onWarning);
        }

        internal static string OpenWithFallback(Func<string> existingLauncher, Func<string> ensureLauncher, Action<string> startLauncher, Func<string> openDirect, Action<string> onWarning)
        {
            string warning;
            string existing;
            try
            {
                existing = existingLauncher();
            }
            catch (Exception error)
            {
                // Discovery can also fail when access to the helper folder is
                // restricted. It must not block a separate official app entry.
                warning = error.Message;
                return OpenAfterLauncherFailure(warning, openDirect, onWarning);
            }
            if (existing == null) return openDirect();
            try
            {
                startLauncher(ensureLauncher());
                return "中文启动组件已请求运行；请确认 Codex 窗口是否出现。";
            }
            catch (Exception error) { warning = error.Message; }
            return OpenAfterLauncherFailure(warning, openDirect, onWarning);
        }

        private static string OpenAfterLauncherFailure(string failure, Func<string> openDirect, Action<string> onWarning)
        {
            string warning = "中文启动组件更新或运行未完成，本次启动前中文检查未执行；将尝试直接打开官方 Codex。" + Environment.NewLine + failure;
            if (onWarning != null) onWarning(warning);
            try
            {
                return openDirect() + Environment.NewLine + "本次使用官方直接启动入口；中文自动检查组件尚待修复，请验证窗口及语言。";
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(warning + Environment.NewLine + "官方直接启动也未完成：" + error.Message, error);
            }
        }

        internal static string OpenDirect()
        {
            InstalledDesktopApp app = FindInstalledApp();
            try { string result = DesktopLaunchSupport.Open(app); ChineseMaintenanceSupport.RecordLaunch(result); return result; }
            catch (Exception error) { ChineseMaintenanceSupport.RecordLaunch(error.Message); throw; }
        }
    }
}
