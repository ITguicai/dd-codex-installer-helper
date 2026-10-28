using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal sealed class OrganizationProbeResult
    {
        public bool Complete { get; set; }
        public string Outcome { get; set; }
        public List<string> Lines { get; set; }
        public OrganizationProbeResult() { Lines = new List<string>(); Outcome = "not-started"; }
    }

    internal static class OrganizationProbeSupport
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref uint length, StringBuilder name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackagePath(ref uint length, StringBuilder path);
        private const string Family = "OpenAI.Codex_2p2nqsd0c76g0";
        private const string Command = "--probe-organization-context";

        internal static bool HandleCommand(string[] args)
        {
            if (args.Length == 0 || args[0] != Command) return false;
            if (args.Length != 2) return true;
            try
            {
                string reportPath = Path.GetFullPath(args[1]);
                // The parent creates one unique directory containing only this
                // sanitized result. Never accept a credential/config output path.
                var directory = new DirectoryInfo(Path.GetDirectoryName(reportPath));
                if (Path.GetFileName(reportPath) != "result.json" || !Regex.IsMatch(directory.Name, "^[a-f0-9]{32}$") || directory.Parent.Name != "organization-probes" || directory.Parent.Parent.Name != "DDCodexInstallerHelper" || File.Exists(reportPath)) return true;
                OrganizationProbeResult result = RunCurrentPackage();
                string temporary = reportPath + ".tmp";
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(result), new UTF8Encoding(false));
                File.Move(temporary, reportPath);
            }
            catch { /* A missing result is reported by the parent without raw exceptions. */ }
            return true;
        }

        internal static OrganizationProbeResult RunCurrentPackage()
        {
            var result = new OrganizationProbeResult();
            try
            {
                uint length = 0;
                if (GetCurrentPackageFamilyName(ref length, null) != 122 || length < 2) throw new InvalidOperationException();
                var family = new StringBuilder((int)length);
                if (GetCurrentPackageFamilyName(ref length, family) != 0 || family.ToString() != Family) throw new InvalidOperationException();
                length = 0;
                if (GetCurrentPackagePath(ref length, null) != 122 || length < 2) throw new InvalidOperationException();
                var root = new StringBuilder((int)length);
                if (GetCurrentPackagePath(ref length, root) != 0 || !Path.IsPathRooted(root.ToString())) throw new InvalidOperationException();
                result.Lines.Add("应用包身份：已确认是当前 Codex；未使用普通 PowerShell 直接执行 WindowsApps 辅助程序。");
                var manifest = new System.Xml.XmlDocument { XmlResolver = null };
                manifest.Load(Path.Combine(root.ToString(), "AppxManifest.xml"));
                var identity = manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
                if (identity == null || identity.Attributes["Name"].Value != "OpenAI.Codex" || identity.Attributes["Publisher"].Value != "CN=50BDFD77-8903-4850-9FFE-6E8522F64D5B") throw new InvalidOperationException();
                string version = identity.Attributes["Version"].Value;
                if (!Regex.IsMatch(version, "^\\d+\\.\\d+\\.\\d+\\.\\d+$")) throw new InvalidOperationException();
                result.Lines.Add("后台检查客户端版本：" + version);
                string executable = Path.Combine(root.ToString(), "app", "resources", "codex.exe");
                OrganizationProbeResult probe = RunAt(executable, version, Path.Combine(root.ToString(), "app"), 35000);
                result.Lines.AddRange(probe.Lines); result.Complete = probe.Complete; result.Outcome = probe.Outcome;
            }
            catch
            {
                result.Outcome = "package-context-unavailable";
                result.Lines.Add("未获得匹配的 Codex 包身份或清单，未执行后台测试。请用安装 Codex 的同一 Windows 用户运行助手。");
            }
            return result;
        }

        internal static OrganizationProbeResult Inspect()
        {
            var result = new OrganizationProbeResult();
            result.Lines.Add("深入检查会带 Codex 包身份启动一个独立官方后台，仅发送初始化、读取组织要求和读取配置请求；不创建聊天、不调用模型、不退出登录。");
            result.Lines.Add("助手不写入配置或凭据；官方后台启动时可能正常记录日志、缓存或刷新现有登录。只关闭本次创建的测试后台，不结束现有 Codex。");
            result.Lines.Add("此项检查包内 Windows 后台，不等同于桌面完整启动；GUI 若选择其他运行环境或更新后的核心，结果可能不同。");
            try
            {
                InstalledDesktopApp app = DesktopShortcutSupport.FindInstalledApp();
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDCodexInstallerHelper", "organization-probes", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                string reportPath = Path.Combine(directory, "result.json");
                string helper = Assembly.GetExecutingAssembly().Location;
                string script = "Invoke-CommandInDesktopPackage -PackageFamilyName '" + Family + "' -AppId '" + app.AppUserModelId.Substring(app.AppUserModelId.IndexOf('!') + 1) + "' -Command " + QuotePS(helper) + " -Args " + QuotePS(Command + " " + QuoteArgument(reportPath)) + " -PreventBreakaway -ErrorAction Stop";
                RunShell(script, 20000);
                var elapsed = Stopwatch.StartNew();
                while (!File.Exists(reportPath) && elapsed.ElapsedMilliseconds < 115000) System.Threading.Thread.Sleep(150);
                if (!File.Exists(reportPath))
                {
                    result.Outcome = "probe-result-timeout";
                    result.Lines.Add("未在时限内收到包身份测试结果；未把它当作组织设置读取成功，也未结束现有客户端。");
                    return result;
                }
                if (new FileInfo(reportPath).Length > 65536) throw new InvalidOperationException();
                var probe = new JavaScriptSerializer().Deserialize<OrganizationProbeResult>(File.ReadAllText(reportPath, Encoding.UTF8));
                if (probe == null || probe.Lines == null || probe.Lines.Count > 100 || !Regex.IsMatch(probe.Outcome ?? "", "^[a-z-]{1,80}$")) throw new InvalidOperationException();
                result.Lines.AddRange(probe.Lines); result.Outcome = probe.Outcome; result.Complete = probe.Complete;
                result.Lines.Add("后台检查结论代码：" + probe.Outcome + "。三步通过也不代表图形界面已恢复。");
            }
            catch
            {
                result.Outcome = "probe-launch-failed";
                result.Lines.Add("带包身份的检查未能启动。请确认仍使用原安装用户，且系统提供 Invoke-CommandInDesktopPackage；助手未改权限或系统策略。");
            }
            return result;
        }

        internal static OrganizationProbeResult RunAt(string executable, string appVersion, string workingDirectory, int timeoutMs)
        {
            var result = new OrganizationProbeResult();
            Process child = null;
            StreamWriter input = null;
            string stage = "initialize";
            bool parseFailed = false;
            int stderrLines = 0, outputBytes = 0;
            try
            {
                if (!File.Exists(executable) || new FileInfo(executable).Length < 2) throw new FileNotFoundException();
                var start = new ProcessStartInfo(executable, "app-server") {
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workingDirectory
                };
                start.EnvironmentVariables["LOG_FORMAT"] = "json";
                start.EnvironmentVariables["RUST_LOG"] = "warn";
                start.EnvironmentVariables["CODEX_INTERNAL_ORIGINATOR_OVERRIDE"] = "codex_desktop";
                // Keep existing configuration/auth/proxy and managed requirements.
                // No -c, sandbox/policy override, profile replacement or auth writes.
                child = new Process { StartInfo = start };
                child.ErrorDataReceived += (sender, args) => { if (args.Data != null) System.Threading.Interlocked.Increment(ref stderrLines); };
                child.Start(); child.BeginErrorReadLine();
                // JSONL requires UTF-8 without a leading BOM. Framework's
                // default redirected stdin writer can emit one on first flush.
                input = new StreamWriter(child.StandardInput.BaseStream, new UTF8Encoding(false));
                input.AutoFlush = true;
                Send(input, new { id = "initialize", method = "initialize", @params = new { clientInfo = new { name = "codex_desktop", title = "Codex Desktop", version = appVersion }, capabilities = new { experimentalApi = true } } });
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    Task<string> read = child.StandardOutput.ReadLineAsync();
                    int remaining = timeoutMs - (int)deadline.ElapsedMilliseconds;
                    if (remaining <= 0 || !read.Wait(remaining))
                    {
                        result.Outcome = "timeout"; result.Lines.Add("后台步骤 " + stage + "：超时；未输出原始日志。"); break;
                    }
                    string line = read.Result;
                    if (line == null)
                    {
                        result.Outcome = "process-exit";
                        result.Lines.Add("后台步骤 " + stage + "：输出已关闭，后台未完成该步骤。");
                        if (child.WaitForExit(2000)) result.Lines.Add("本次测试后台退出码：" + child.ExitCode + "（0x" + child.ExitCode.ToString("X8") + "）。");
                        break;
                    }
                    outputBytes += Encoding.UTF8.GetByteCount(line);
                    if (line.Length > 2097152 || outputBytes > 8388608) { result.Outcome = "output-limit"; result.Lines.Add("后台输出超过检查上限；内容未保存。"); break; }
                    Dictionary<string, object> message;
                    try { message = new JavaScriptSerializer { MaxJsonLength = 2097152 }.Deserialize<Dictionary<string, object>>(line); }
                    catch { parseFailed = true; continue; }
                    if (message == null || !message.ContainsKey("id") || Convert.ToString(message["id"]) != stage) continue;
                    if (message.ContainsKey("error") && message["error"] != null)
                    {
                        result.Outcome = "rpc-error"; result.Lines.Add("后台步骤 " + stage + "：返回错误。");
                        result.Lines.AddRange(SummarizeError(message["error"])); break;
                    }
                    if (!message.ContainsKey("result")) continue;
                    result.Lines.Add("后台步骤 " + stage + "：通过。");
                    if (stage == "initialize")
                    {
                        var value = message["result"] as Dictionary<string, object>;
                        if (value != null && value.ContainsKey("userAgent"))
                        {
                            Match version = Regex.Match(Convert.ToString(value["userAgent"]), @"\b0\.\d+\.\d+(?:-[A-Za-z0-9.+_-]+)?");
                            if (version.Success) result.Lines.Add("实际后台版本：" + version.Value);
                        }
                        Send(input, new { method = "initialized" });
                        stage = "requirements"; Send(input, new { id = stage, method = "configRequirements/read", @params = new { } });
                    }
                    else if (stage == "requirements")
                    {
                        result.Lines.Add(RequirementsShape(message["result"]));
                        stage = "configuration"; Send(input, new { id = stage, method = "config/read", @params = new { includeLayers = false } });
                    }
                    else { result.Complete = true; result.Outcome = "read-requests-completed"; break; }
                    deadline.Restart();
                }
            }
            catch (Exception error)
            {
                result.Outcome = "process-error";
                var native = error as System.ComponentModel.Win32Exception;
                result.Lines.Add("后台步骤 " + stage + "：进程或读取失败；HRESULT 0x" + error.HResult.ToString("X8") + (native == null ? "。" : "；Win32 " + native.NativeErrorCode + "。"));
            }
            finally
            {
                if (child != null)
                {
                    try { if (input != null) input.Dispose(); else child.StandardInput.Close(); } catch { }
                    try { if (!child.WaitForExit(1500)) { child.Kill(); child.WaitForExit(1500); } } catch { }
                    child.Dispose();
                }
                result.Lines.Add("本次测试后台 stderr 记录行数：" + stderrLines + "；原文未写入报告。" + (parseFailed ? "存在未识别的输出行，已省略。" : ""));
            }
            return result;
        }

        internal static List<string> SummarizeError(object raw)
        {
            var lines = new List<string>();
            var error = raw as Dictionary<string, object>;
            if (error == null) { lines.Add("错误类别：无法识别；原始错误已省略。"); return lines; }
            int number;
            if (error.ContainsKey("code") && Int32.TryParse(Convert.ToString(error["code"]), out number)) lines.Add("RPC 错误码：" + number + "。");
            var data = error.ContainsKey("data") ? error["data"] as Dictionary<string, object> : null;
            if (data != null)
            {
                foreach (string key in new[] { "reason", "errorCode" })
                {
                    string value = data.ContainsKey(key) ? data[key] as string : null;
                    if (value == "cloudRequirements" || value == "cloudConfigBundle" || value == "Timeout" || value == "RequestFailed") lines.Add(key + "：" + value + "。");
                }
                if (data.ContainsKey("statusCode") && Int32.TryParse(Convert.ToString(data["statusCode"]), out number) && number >= 100 && number <= 599) lines.Add("HTTP 状态码：" + number + "。");
            }
            string text = error.ContainsKey("message") ? error["message"] as string : null;
            if (text != null)
            {
                if (Regex.IsMatch(text, "invalid configuration|failed to read configuration layers|error parsing|TOML", RegexOptions.IgnoreCase)) lines.Add("固定错误类别：配置加载/解析失败；具体配置和路径已省略。");
                if (Regex.IsMatch(text, "refresh token|authentication|unauthorized|not authenticated", RegexOptions.IgnoreCase)) lines.Add("固定错误类别：认证相关；令牌内容已省略。");
                if (Regex.IsMatch(text, "timed out|timeout", RegexOptions.IgnoreCase)) lines.Add("固定错误类别：超时。");
                if (Regex.IsMatch(text, "access is denied|permission denied|拒绝访问", RegexOptions.IgnoreCase)) lines.Add("固定错误类别：访问被拒绝。");
            }
            lines.Add("错误原文、地址、配置、密钥与账号内容均已省略。");
            return lines;
        }

        internal static string RequirementsShape(object raw)
        {
            var root = raw as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("requirements")) return "组织要求响应结构：未发现 requirements 字段；可能不符合桌面启动检查预期。";
            if (root["requirements"] == null) return "组织要求响应结构：requirements=null；不输出组织要求内容。";
            var requirements = root["requirements"] as Dictionary<string, object>;
            if (requirements == null || !requirements.ContainsKey("application")) return "组织要求响应结构：application 字段缺失；需要结合后台版本核对桌面兼容性。";
            if (requirements["application"] == null) return "组织要求响应结构：application=null；不输出组织要求内容。";
            var application = requirements["application"] as Dictionary<string, object>;
            if (application == null || !application.ContainsKey("network")) return "组织要求响应结构：network 字段缺失或格式未识别。";
            if (application["network"] == null) return "组织要求响应结构：network=null；不输出组织要求内容。";
            var network = application["network"] as Dictionary<string, object>;
            return network != null && network.ContainsKey("enabled") && network["enabled"] is bool && network.ContainsKey("domains") && network["domains"] is Dictionary<string, object>
                ? "组织要求响应结构：包含 network.enabled 与 domains；仅检查结构，未输出域名或修改限制。"
                : "组织要求响应结构：network 格式未识别；可能不符合桌面校验预期。";
        }

        private static void Send(StreamWriter input, object message) { input.WriteLine(new JavaScriptSerializer().Serialize(message)); }
        private static string QuotePS(string value) { return "'" + value.Replace("'", "''") + "'"; }
        private static string QuoteArgument(string value) { return "\"" + value.Replace("\"", "") + "\""; }

        internal static string OpenCli(string projectDirectory)
        {
            string directory = Path.GetFullPath(projectDirectory);
            if (!Directory.Exists(directory)) throw new InvalidOperationException("请选择已有项目目录。");
            InstalledDesktopApp app = DesktopShortcutSupport.FindInstalledApp();
            string core = Path.Combine(app.InstallLocation, "app", "resources", "codex.exe");
            if (!File.Exists(core) || new FileInfo(core).Length == 0) throw new InvalidOperationException("官方命令行后台缺失，请通过官方安装器修复程序文件。");
            string script = CliLaunchScript(app, directory);
            RunShell(script, 20000);
            return "已请求打开官方命令行版，请在新窗口确认出现输入界面。继续读取原有模型/API 配置与组织要求，未关闭沙箱或策略。若正常进入，可先在该窗口使用；这不代表桌面界面已经修复。";
        }

        internal static string CliLaunchScript(InstalledDesktopApp app, string directory)
        {
            string core = Path.Combine(app.InstallLocation, "app", "resources", "codex.exe");
            string body = "Set-Location -LiteralPath " + QuotePS(directory) + "; & " + QuotePS(core) + " '-C' " + QuotePS(directory);
            string arguments = "-NoLogo -NoProfile -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(body));
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            string script = "Invoke-CommandInDesktopPackage -PackageFamilyName '" + Family + "' -AppId '" + app.AppUserModelId.Substring(app.AppUserModelId.IndexOf('!') + 1) + "' -Command " + QuotePS(powershell) + " -Args " + QuotePS(arguments) + " -PreventBreakaway -ErrorAction Stop";
            return script;
        }

        private static void RunShell(string script, int timeout)
        {
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout)) { try { process.Kill(); } catch { } throw new InvalidOperationException("包身份启动请求超时；未结束 Codex。"); }
                output.GetAwaiter().GetResult(); error.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("包身份启动未完成，PowerShell 退出码 " + process.ExitCode + "；未输出原始系统错误。");
            }
        }
    }
}
