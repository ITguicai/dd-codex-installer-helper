using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal static class DesktopRepairSupport
    {
        internal static string Inspect()
        {
            string environment = DesktopLaunchSupport.EnvironmentSummary(null);
            try { environment = DesktopLaunchSupport.EnvironmentSummary(Path.Combine(DesktopShortcutSupport.FindInstalledApp().InstallLocation, "app")); }
            catch { /* A broken registration is itself a reason to inspect. */ }
            try { return environment + Environment.NewLine + Run("inspect", Execute); }
            catch (InvalidOperationException error) { throw new InvalidOperationException(environment + Environment.NewLine + error.Message, error); }
        }

        internal static string Repair()
        {
            if (OfflineChineseSupport.IsDesktopRunning()) throw new InvalidOperationException("请先完全退出 Codex，再修复启动注册；助手不会强制结束任务。");
            return Run("repair", Execute);
        }

        internal static string Script()
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DDCodexInstallerHelper.desktop-startup.ps1"))
            {
                if (resource == null) throw new InvalidOperationException("助手启动检查组件缺失，请重新获取完整助手。");
                using (var reader = new StreamReader(resource, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        internal static string Run(string mode, Func<string, string> execute)
        {
            if (mode != "inspect" && mode != "repair") throw new ArgumentException("Unknown startup operation");
            string command = "& {\n" + Script() + "\n} -Mode '" + mode + "'";
            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(execute(command));
            if (result == null || !result.ContainsKey("ok") || !(result["ok"] is bool) || !result.ContainsKey("lines") || !(result["lines"] is IEnumerable) || result["lines"] is string)
                throw new InvalidOperationException("启动检查组件返回格式异常，请重新获取助手。");
            var report = new StringBuilder();
            foreach (object line in (IEnumerable)result["lines"]) if (line is string) report.AppendLine((string)line);
            if (!(bool)result["ok"]) throw new InvalidOperationException(report.ToString().Trim());
            return report.ToString().Trim();
        }

        private static string Execute(string command)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)))
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (Process process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("启动检查或注册命令未在两分钟内返回；未强制结束 Codex。请检查日志后重试。");
                }
                string text = output.GetAwaiter().GetResult();
                error.GetAwaiter().GetResult();
                if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("启动检查组件未完成，Windows PowerShell 退出码：" + process.ExitCode + "。");
                return text.Trim();
            }
        }
    }
}
