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
    internal sealed class OrganizationCheckResult
    {
        internal bool Complete;
        internal string Report;
    }

    internal static class OrganizationCheckSupport
    {
        internal static string Script()
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DDCodexInstallerHelper.organization-check.ps1"))
            {
                if (resource == null) throw new InvalidOperationException("组织设置检查组件缺失，请重新获取完整助手。");
                using (var reader = new StreamReader(resource, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        internal static OrganizationCheckResult Inspect()
        {
            return Run(Execute);
        }

        internal static OrganizationCheckResult InspectWithProbe()
        {
            OrganizationCheckResult basis;
            try { basis = Inspect(); }
            catch { basis = new OrganizationCheckResult { Complete = false, Report = "静态配置/日志采集未完成；仍继续测试官方后台。未输出原始错误。" }; }
            OrganizationProbeResult live = OrganizationProbeSupport.Inspect();
            var report = new StringBuilder(basis.Report);
            report.AppendLine(); report.AppendLine(); report.AppendLine("──────── 应用包身份下的后台实际读取测试 ────────");
            foreach (string line in live.Lines) report.AppendLine(line);
            report.AppendLine("若三步通过而桌面仍弹窗，请保留报告，继续核对桌面校验和实际核心选择；无需再次修改 WSL 或注册。可尝试“打开命令行版”临时使用，仍按原配置与组织要求运行。");
            return new OrganizationCheckResult { Complete = basis.Complete && live.Complete, Report = report.ToString().Trim() };
        }

        internal static OrganizationCheckResult Run(Func<string, string> execute)
        {
            string command = "& {\n" + Script() + "\n}";
            var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(execute(command));
            if (result == null || !result.ContainsKey("ok") || !(result["ok"] is bool) || !result.ContainsKey("lines") || !(result["lines"] is IEnumerable) || result["lines"] is string)
                throw new InvalidOperationException("组织设置检查组件返回格式异常，请重新获取助手。");
            var report = new StringBuilder();
            report.AppendLine("DD Codex 安装助手 " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " · 组织设置检查");
            report.AppendLine("检查时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (object line in (IEnumerable)result["lines"])
            {
                if (!(line is string)) throw new InvalidOperationException("组织设置检查组件返回日志格式异常。");
                report.AppendLine((string)line);
            }
            report.Append((bool)result["ok"] ? "采集完成；这不代表启动故障已修复。" : "部分项目未能采集；请保留已有诊断结果。");
            return new OrganizationCheckResult { Complete = (bool)result["ok"], Report = report.ToString() };
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
                if (!process.WaitForExit(90000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("组织设置检查超过 90 秒，已停止本次检查进程；未结束 Codex。请稍后重试。");
                }
                string text = output.GetAwaiter().GetResult();
                error.GetAwaiter().GetResult();
                if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(text))
                    throw new InvalidOperationException("组织设置检查组件未完成，Windows PowerShell 退出码：" + process.ExitCode + "。未输出原始系统错误。");
                return text.Trim();
            }
        }
    }
}
