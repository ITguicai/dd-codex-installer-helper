using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal sealed class OfflineChineseIncompleteException : InvalidOperationException
    {
        internal readonly int Completed;
        internal OfflineChineseIncompleteException(string report, int completed) : base(report) { Completed = completed; }
    }

    internal static class OfflineChineseSupport
    {
        internal static bool IsDesktopRunning()
        {
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        string name = process.ProcessName;
                        if (name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)) return true;
                        if (name.Equals("Codex", StringComparison.OrdinalIgnoreCase))
                        {
                            string executable = process.MainModule.FileName;
                            if (executable.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                executable.IndexOf("\\app\\Codex.exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        }
                    }
                    catch { /* CLI worker processes can be inaccessible. */ }
                }
            }
            return false;
        }

        internal static string Enable()
        {
            if (IsDesktopRunning()) throw new InvalidOperationException("请先完全退出 Codex，再点击“启用离线中文”。助手不会强制结束您的任务。");
            string appRoot = FindInstalledApp();
            return EnableProfilesAt(appRoot, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DesktopProfileSupport.ConfigPathForCurrentUser(),
                Environment.GetEnvironmentVariable("CODEX_ELECTRON_USER_DATA_PATH"));
        }

        internal static string EnableProfilesAt(string appRoot, string roamingRoot, string localRoot, string configPath, string customData)
        {
            string config = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : "";
            // Respect the user's privacy choice: disabled analytics disables
            // Statsig local storage in the supported client builds.
            if (System.Text.RegularExpressions.Regex.IsMatch(config, @"(?ms)^\s*\[analytics\][^\r\n]*\r?\n(?:(?!^\s*\[).)*?^\s*enabled\s*=\s*false\s*(?:#.*)?$"))
                throw new InvalidOperationException("当前 Codex 关闭了分析存储，客户端不会读取语言缓存。助手保留此隐私设置；本离线方案暂不支持该配置。");
            List<DesktopProfile> targets = DesktopProfileSupport.WriteTargets(DesktopProfileSupport.ResolveAt(appRoot, roamingRoot, localRoot, customData));
            var result = new StringBuilder();
            result.AppendLine("缓存目录处理范围：" + targets.Count + " 个；各目录独立保留本机启动标识。");
            int completed = 0;
            var failures = new List<string>();
            foreach (DesktopProfile target in targets)
            {
                result.AppendLine("缓存位置类型：" + target.Label);
                try { result.AppendLine(RunComponent(appRoot, roamingRoot, "--enable", target.UserDataDirectory).Trim()); completed++; }
                catch (Exception ex)
                {
                    result.AppendLine("此目录未完成：" + target.CacheDirectory + "。" + Environment.NewLine + ex.Message);
                    failures.Add(target.CacheDirectory);
                }
            }
            result.AppendLine("中文缓存处理结果：已完成 " + completed + "/" + targets.Count + " 个目录；未完成 " + failures.Count + " 个目录。");
            if (completed == 0)
                throw new OfflineChineseIncompleteException(result + "所有目录均未完成；未改写语言设置，也未清空或重建原缓存。", 0);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            if (File.Exists(configPath)) File.Copy(configPath, configPath + ".dd-chinese-backup-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), false);
            File.WriteAllText(configPath, MainForm.ConfigureDesktopLocale(config), new UTF8Encoding(false));
            result.AppendLine("[desktop] 简体中文语言选项已写入。");
            result.AppendLine("语言设置文件：" + configPath);
            result.Append("以上确认本地配置写入；请打开 Codex 后点击“检查中文状态”，核对实际启动目录与两处日志。界面中文尚未验收。");
            if (failures.Count > 0)
                throw new OfflineChineseIncompleteException(result + Environment.NewLine + "部分目录未完成；已继续处理其余目录，未清空或重建失败目录。请打开客户端验证实际使用的目录及中文界面。", completed);
            return result.ToString().Trim();
        }

        internal static string Inspect()
        {
            string appRoot = FindInstalledApp();
            string roamingRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string configPath = DesktopProfileSupport.ConfigPathForCurrentUser();
            string customData = Environment.GetEnvironmentVariable("CODEX_ELECTRON_USER_DATA_PATH");
            var candidates = DesktopProfileSupport.ResolveAt(appRoot, roamingRoot, localRoot, customData);
            return InspectProfilesAt(appRoot, roamingRoot, localRoot, configPath, customData) + Environment.NewLine + DesktopProfileSupport.RuntimeSummary(appRoot, candidates) +
                Environment.NewLine + ChineseMaintenanceSupport.InspectForCurrentUser();
        }

        // Diagnostics expose only language state and credential presence, never
        // credential contents, account identities, or raw client log lines.
        internal static string InspectAt(string appRoot, string roamingRoot, string configPath, string logRoot)
        {
            var report = new StringBuilder();
            AppendConfigSummary(report, appRoot, configPath);
            try { report.AppendLine(RunComponent(appRoot, roamingRoot, "--inspect").Trim()); }
            catch (Exception ex) { report.AppendLine("缓存快照检查未完成：" + ex.Message + "；未修改客户缓存。"); }
            AppendLanguageLogSummary(report, logRoot);
            report.AppendLine("以上为启动配置诊断，实际中文显示请以客户端界面为准。");
            return report.ToString().Trim();
        }

        internal static string InspectProfilesAt(string appRoot, string roamingRoot, string localRoot, string configPath, string customData)
        {
            var report = new StringBuilder();
            AppendConfigSummary(report, appRoot, configPath);
            foreach (DesktopProfile profile in DesktopProfileSupport.ResolveAt(appRoot, roamingRoot, localRoot, customData))
            {
                report.AppendLine("缓存位置类型：" + profile.Label + "；" + (profile.Initialized ? "已有客户端数据" : "尚未发现客户端数据"));
                try { report.AppendLine(RunComponent(appRoot, roamingRoot, "--inspect", profile.UserDataDirectory).Trim()); }
                catch (Exception ex) { report.AppendLine("缓存快照检查未完成：" + profile.CacheDirectory + "。" + Environment.NewLine + ex.Message + "；未修改客户缓存。"); }
            }
            foreach (string root in DesktopProfileSupport.LogRootsAt(appRoot, localRoot)) AppendLanguageLogSummary(report, root);
            report.AppendLine("以上只确认配置、缓存和日志，不能把缓存回读成功等同于客户端已显示中文。实际中文显示请以界面为准。");
            return report.ToString().Trim();
        }

        private static void AppendConfigSummary(StringBuilder report, string appRoot, string configPath)
        {
            report.AppendLine("助手版本：" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + "；客户端目录：" + new DirectoryInfo(Path.GetDirectoryName(appRoot)).Name);
            report.AppendLine(DesktopLaunchSupport.EnvironmentSummary(appRoot));
            report.AppendLine("语言设置文件：" + configPath);
            string config = File.Exists(configPath) ? File.ReadAllText(configPath, Encoding.UTF8) : "";
            Match desktop = Regex.Match(config, @"(?ms)^\s*\[desktop\][^\r\n]*\r?\n(?:(?!^\s*\[).)*");
            Match locale = Regex.Match(desktop.Value, @"(?m)^\s*localeOverride\s*=\s*[""']([a-zA-Z0-9-]+)[""']");
            report.AppendLine("界面语言设置：" + (locale.Success ? locale.Groups[1].Value : "未发现 desktop.localeOverride"));
            bool storageDisabled = Regex.IsMatch(config, @"(?ms)^\s*\[analytics\][^\r\n]*\r?\n(?:(?!^\s*\[).)*?^\s*enabled\s*=\s*false\s*(?:#.*)?$");
            report.AppendLine("分析存储配置：" + (storageDisabled ? "已关闭，客户端不会读取语言缓存" : "未显式关闭"));
            string authPath = Path.Combine(Path.GetDirectoryName(configPath), "auth.json");
            bool apiKey = false, loginTokens = false;
            if (File.Exists(authPath))
            {
                try
                {
                    var auth = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(authPath, Encoding.UTF8));
                    object key, tokens;
                    apiKey = auth.TryGetValue("OPENAI_API_KEY", out key) && key is string && !String.IsNullOrWhiteSpace((string)key);
                    loginTokens = auth.TryGetValue("tokens", out tokens) && tokens != null;
                }
                catch { report.AppendLine("凭据文件：格式无法识别（未输出内容）"); }
            }
            report.AppendLine("凭据存在状态：API 密钥 " + (apiKey ? "有" : "无") + "；登录令牌 " + (loginTokens ? "有" : "无") + "；进程 API 环境变量 " +
                (String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) ? "无" : "有") + "（不代表运行时登录方式）");
            string customHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (!String.IsNullOrWhiteSpace(customHome))
                report.AppendLine("检测到 CODEX_HOME：" + customHome + "；请核对客户端是否使用这个配置目录。");
        }

        private static void AppendLanguageLogSummary(StringBuilder report, string logRoot)
        {
            report.AppendLine("客户端日志目录：" + logRoot);
            if (!Directory.Exists(logRoot)) { report.AppendLine("客户端日志：尚未发现。"); return; }
            string[] files;
            try { files = Directory.GetFiles(logRoot, "*.log", SearchOption.AllDirectories); }
            catch { report.AppendLine("客户端日志：目录暂时无法读取。"); return; }
            Array.Sort(files, (left, right) => File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));
            if (files.Length == 0) { report.AppendLine("客户端日志：尚未发现。"); return; }
            int loadFailures = 0, resolveFailures = 0, missingCache = 0, read = 0;
            for (int i = 0; i < Math.Min(files.Length, 3); i++)
            {
                try
                {
                    using (var stream = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (stream.Length > 1048576) stream.Seek(-1048576, SeekOrigin.End);
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string text = reader.ReadToEnd();
                            loadFailures += Regex.Matches(text, "Failed to load locale messages").Count;
                            resolveFailures += Regex.Matches(text, "Failed to resolve preferred locale").Count;
                            missingCache += Regex.Matches(text, "NoCachedValues").Count;
                        }
                    }
                    read++;
                }
                catch { /* A rotating or protected log is not a repair failure. */ }
            }
            report.AppendLine("最近日志更新时间：" + File.GetLastWriteTime(files[0]).ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("最近 " + read + " 个日志尾部：语言资源加载失败 " + loadFailures + " 次；语言解析失败 " + resolveFailures + " 次；NoCachedValues " + missingCache + " 次。未出现错误不代表中文已显示。");
        }

        private static string FindInstalledApp()
        {
            return Path.Combine(DesktopShortcutSupport.FindInstalledApp().InstallLocation, "app");
        }

        // Kept separate so verification can use isolated profiles only.
        internal static string EnableAt(string appRoot, string roamingRoot)
        {
            return RunComponent(appRoot, roamingRoot, "");
        }

        internal static bool EnsureAt(string appRoot, string roamingRoot, string userDataRoot)
        {
            string state = RunComponent(appRoot, roamingRoot, "--inspect", userDataRoot);
            bool ready = state.Contains("主进程与界面启动标识：一致") &&
                state.Contains("未登录/无需登录模式中文缓存：2/2 条启用") && state.Contains("API 密钥模式中文缓存：2/2 条启用");
            if (ready) return false;
            RunComponent(appRoot, roamingRoot, "--enable", userDataRoot);
            return true;
        }

        private static string RunComponent(string appRoot, string roamingRoot, string mode, string userDataRoot = null)
        {
            string packageName = new DirectoryInfo(Path.GetDirectoryName(appRoot)).Name;
            string runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDCodexInstallerHelper", "runtime", packageName);
            string node = PrepareRuntimeAt(appRoot, runtime);
            string taskDir = Path.Combine(Path.GetTempPath(), "DDCodexChinese-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(taskDir);
            string scriptPath = Path.Combine(taskDir, "offline-chinese.cjs");
            try
            {
                using (Stream embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("DDCodexInstallerHelper.offline-chinese.cjs"))
                {
                    if (embedded == null) throw new InvalidOperationException("助手内置的离线中文组件缺失，请重新获取完整助手。");
                    using (FileStream output = File.Create(scriptPath)) embedded.CopyTo(output);
                }
                string operation = String.IsNullOrEmpty(mode) ? "--enable" : mode;
                if (userDataRoot != null) operation += " " + Quote(userDataRoot);
                return Run(node, Quote(scriptPath) + " " + Quote(Path.Combine(runtime, "node_modules")) + " " + Quote(appRoot) + " " + Quote(roamingRoot) + " " + operation, 120000);
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
                if (Directory.Exists(taskDir)) Directory.Delete(taskDir, false);
            }
        }

        internal static string PrepareRuntimeAt(string appRoot, string runtime)
        {
            string source = Path.Combine(appRoot, "resources", "cua_node", "bin");
            string node = Path.Combine(runtime, "node.exe");
            if (!File.Exists(Path.Combine(source, "node.exe")))
                throw new InvalidOperationException("此客户端缺少离线配置运行组件，请更新官方 Codex 后重试。");
            Directory.CreateDirectory(runtime);
            if (!File.Exists(node) || new FileInfo(node).Length != new FileInfo(Path.Combine(source, "node.exe")).Length)
                PublicFileSupport.Copy(Path.Combine(source, "node.exe"), node, true);
            CopyModule("classic-level", Path.Combine(source, "node_modules"), Path.Combine(runtime, "node_modules"), new HashSet<string>());
            return node;
        }

        private static void CopyModule(string name, string sourceRoot, string destinationRoot, HashSet<string> visited)
        {
            if (!visited.Add(name)) return;
            string source = Path.Combine(sourceRoot, name.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.Combine(destinationRoot, name.Replace('/', Path.DirectorySeparatorChar));
            string manifest = Path.Combine(source, "package.json");
            if (!File.Exists(manifest)) throw new InvalidOperationException("官方客户端缺少本地组件：" + name);
            CopyDirectory(source, destination);
            var serializer = new JavaScriptSerializer();
            var package = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifest));
            object dependencies;
            if (package.TryGetValue("dependencies", out dependencies))
            {
                var entries = dependencies as Dictionary<string, object>;
                if (entries != null)
                    foreach (string dependency in entries.Keys) CopyModule(dependency, sourceRoot, destinationRoot, visited);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                string extension = Path.GetExtension(file);
                string name = Path.GetFileName(file);
                if (extension != ".js" && extension != ".json" && extension != ".node" && !name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(destination, name);
                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(file).Length) PublicFileSupport.Copy(file, target, true);
            }
            foreach (string directory in Directory.GetDirectories(source))
            {
                string name = Path.GetFileName(directory);
                if (name == "node_modules" || name == "deps" || name == "test" || name == "tests" || name == "docs") continue;
                CopyDirectory(directory, Path.Combine(destination, name));
            }
        }

        private static string Run(string executable, string arguments, int timeout)
        {
            var start = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (Process process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("本地中文配置操作超时，请重试。");
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException((stderr + Environment.NewLine + stdout).Trim());
                return stdout;
            }
        }

        private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    }
}
