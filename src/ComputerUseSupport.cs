using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal static class ComputerUseSupport
    {
        private const string PluginId = "computer-use@openai-bundled";
        private const string InitializeHint = "请先从开始菜单正常打开 Codex 一次，待插件页面加载后完全退出，再点击“安装 Computer Use”。";

        internal static string InstallForCurrentUser(InstalledDesktopApp app)
        {
            if (OfflineChineseSupport.IsDesktopRunning())
                throw new InvalidOperationException("请先完全退出 Codex，再安装 Computer Use；重新打开后插件才会加载。");
            string codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(codexHome))
                codexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            if (!Path.IsPathRooted(codexHome))
                throw new InvalidOperationException("CODEX_HOME 不是绝对路径，请先确认 Codex 实际使用的配置目录。");
            string localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return InstallAt(Path.Combine(app.InstallLocation, "app"), codexHome, localRoot);
        }

        internal static string InstallAt(string appRoot, string codexHome, string localRoot)
        {
            appRoot = Path.GetFullPath(appRoot);
            codexHome = Path.GetFullPath(codexHome);
            string bundledPlugin = Path.Combine(appRoot, "resources", "plugins", "openai-bundled", "plugins", "computer-use", ".codex-plugin", "plugin.json");
            if (!File.Exists(bundledPlugin))
                throw new InvalidOperationException("当前 Codex 未内置 Windows Computer Use 插件，请先更新官方客户端。");
            string bundledVersion = GetString(ReadJson(bundledPlugin), "version");
            string marketplace = Path.Combine(codexHome, ".tmp", "bundled-marketplaces", "openai-bundled");
            string preparedManifest = Path.Combine(marketplace, "plugins", "computer-use", ".codex-plugin", "plugin.json");
            // The reserved marketplace is initialized by the official desktop
            // app. Never register a renamed copy or override its policy.
            if (!File.Exists(preparedManifest) || !File.Exists(Path.Combine(marketplace, ".agents", "plugins", "marketplace.json")))
                throw new InvalidOperationException("官方本地插件库尚未准备完成。" + InitializeHint);
            if (GetString(ReadJson(preparedManifest), "version") != bundledVersion)
                throw new InvalidOperationException("官方本地插件库与已安装客户端版本不一致。" + InitializeHint);

            string cli = PrepareCli(appRoot, localRoot);
            Dictionary<string, object> listing = RunCli(cli, codexHome, "plugin list --marketplace openai-bundled --available --json");
            Dictionary<string, object> plugin = FindPlugin(listing, "installed") ?? FindPlugin(listing, "available");
            if (plugin == null)
                throw new InvalidOperationException("Codex 尚未注册 Computer Use 的官方插件来源。" + InitializeHint);
            if (GetString(plugin, "installPolicy") != "AVAILABLE")
                throw new InvalidOperationException("当前组织或客户端策略不允许由助手安装此插件，请在 Codex 插件页面查看限制或联系管理员。");
            Dictionary<string, object> source = GetObject(plugin, "marketplaceSource");
            if (source == null || GetString(source, "sourceType") != "local" || !SamePath(GetString(source, "source"), marketplace))
                throw new InvalidOperationException("Computer Use 的来源不是客户端准备的官方本地插件库。" + InitializeHint);

            string backup = null;
            if (!GetBool(plugin, "installed") || !GetBool(plugin, "enabled") || GetString(plugin, "version") != bundledVersion)
            {
                string config = Path.Combine(codexHome, "config.toml");
                if (File.Exists(config))
                {
                    backup = config + ".dd-computer-use-backup-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    File.Copy(config, backup, false);
                }
                RunCli(cli, codexHome, "plugin add " + PluginId + " --json");
            }
            listing = RunCli(cli, codexHome, "plugin list --marketplace openai-bundled --json");
            plugin = FindPlugin(listing, "installed");
            if (plugin == null || !GetBool(plugin, "installed") || !GetBool(plugin, "enabled"))
                throw new InvalidOperationException("官方安装命令已结束，但插件尚未处于已安装并启用状态。请在 Codex 插件页面检查 Computer Use 的启用开关。");
            string version = GetString(plugin, "version");
            if (version != bundledVersion || !Regex.IsMatch(version, @"^[a-zA-Z0-9_.-]+$"))
                throw new InvalidOperationException("安装后的 Computer Use 版本与客户端插件库不一致，请重新初始化客户端插件页面后重试。");
            string installedPath = Path.Combine(codexHome, "plugins", "cache", "openai-bundled", "computer-use", version);
            if (!File.Exists(Path.Combine(installedPath, ".codex-plugin", "plugin.json")) ||
                Directory.GetFiles(installedPath, "SKILL.md", SearchOption.AllDirectories).Length == 0)
                throw new InvalidOperationException("插件被标记为已安装，但本地技能文件不完整。请在 Codex 插件页面重新安装 Computer Use。");

            var result = new StringBuilder();
            result.AppendLine("官方 Computer Use 插件已安装并启用；版本：" + version + "。");
            result.AppendLine("插件目录：" + installedPath);
            if (backup != null) result.AppendLine("原配置备份：" + backup);
            result.AppendLine("请打开 Codex，新建聊天并选择 Computer Use，尝试让它打开记事本；首次控制应用时按客户端提示授权。");
            result.AppendLine("这只确认插件安装和启用。若仍提示“组织停用/地区不可用”，功能尚不可用，需在客户端检查权限或联系管理员。");
            result.Append("Computer Use 与设置里的“浏览器”是不同功能，安装此插件不代表浏览器开关已可用。");
            return result.ToString();
        }

        private static string PrepareCli(string appRoot, string localRoot)
        {
            string original = Path.Combine(appRoot, "resources", "codex.exe");
            if (!File.Exists(original)) throw new InvalidOperationException("未找到官方客户端内置的 Codex 命令行组件，请更新客户端。");
            string expected = FileHash(original);
            string binRoot = Path.Combine(localRoot, "OpenAI", "Codex", "bin");
            if (Directory.Exists(binRoot))
            {
                foreach (string directory in Directory.GetDirectories(binRoot))
                {
                    string candidate = Path.Combine(directory, "codex.exe");
                    if (File.Exists(candidate) && new FileInfo(candidate).Length == new FileInfo(original).Length && FileHash(candidate) == expected)
                        return candidate;
                }
            }
            // WindowsApps binaries may not be executable directly. Use a
            // verified, byte-identical local copy, as the desktop client does.
            string runtime = Path.Combine(localRoot, "DDCodexInstallerHelper", "runtime", "computer-use-" + expected.Substring(0, 16));
            Directory.CreateDirectory(runtime);
            string executable = Path.Combine(runtime, "codex.exe");
            if (!File.Exists(executable)) PublicFileSupport.Copy(original, executable, false);
            if (FileHash(executable) != expected)
                throw new InvalidOperationException("本地插件安装组件校验失败。运行组件目录：" + runtime + "。请移走该目录后重试。");
            return executable;
        }

        private static Dictionary<string, object> RunCli(string cli, string codexHome, string arguments)
        {
            var start = new ProcessStartInfo(cli, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = codexHome
            };
            start.EnvironmentVariables["CODEX_HOME"] = codexHome;
            using (Process process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(45000))
                {
                    try { process.Kill(); } catch { }
                    throw new InvalidOperationException("官方插件安装组件执行超时，请关闭助手后重试。");
                }
                string stdout = output.GetAwaiter().GetResult();
                string stderr = error.GetAwaiter().GetResult();
                if (process.ExitCode != 0)
                {
                    // Do not log raw CLI output: config errors can contain keys.
                    if (Regex.IsMatch(stderr, @"(?i)unrecognized subcommand|unexpected argument"))
                        throw new InvalidOperationException("当前客户端尚不支持官方插件安装命令，请更新 Codex 后重试。");
                    if (Regex.IsMatch(stderr, @"(?i)policy|not allowed|disabled|requirement"))
                        throw new InvalidOperationException("官方安装接口报告策略或配置限制，请到 Codex 插件页面检查权限或联系管理员。");
                    throw new InvalidOperationException("官方插件安装命令失败（退出码 " + process.ExitCode + "）。请在 Codex 插件页面尝试安装 Computer Use，核对配置和插件库。原始命令输出未写入日志。");
                }
                try { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(stdout); }
                catch { throw new InvalidOperationException("官方插件接口返回了无法识别的数据，未将原始内容写入日志。请更新 Codex 后重试。"); }
            }
        }

        private static Dictionary<string, object> FindPlugin(Dictionary<string, object> listing, string field)
        {
            object value;
            if (listing == null || !listing.TryGetValue(field, out value)) return null;
            var items = value as IEnumerable;
            if (items == null) return null;
            foreach (object item in items)
            {
                var plugin = item as Dictionary<string, object>;
                if (plugin != null && GetString(plugin, "pluginId") == PluginId) return plugin;
            }
            return null;
        }

        private static Dictionary<string, object> ReadJson(string path)
        {
            try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8)); }
            catch { throw new InvalidOperationException("官方插件描述文件无法读取，请重新初始化 Codex 的插件页面。"); }
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string ?? "" : "";
        }

        private static bool GetBool(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) && value is bool && (bool)value;
        }

        private static bool SamePath(string first, string second)
        {
            if (String.IsNullOrWhiteSpace(first)) return false;
            return String.Equals(Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }

        private static string FileHash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream file = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "");
        }
    }
}
