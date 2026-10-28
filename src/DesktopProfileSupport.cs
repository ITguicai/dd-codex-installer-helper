using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DDCodexInstallerHelper
{
    internal sealed class DesktopProfile
    {
        internal string UserDataDirectory;
        internal string CacheDirectory;
        internal string Label;
        internal bool Initialized;
    }

    internal static class DesktopProfileSupport
    {
        internal static string ConfigPathForCurrentUser()
        {
            string custom = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(custom))
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
            if (!Path.IsPathRooted(custom)) throw new InvalidOperationException("CODEX_HOME 不是绝对路径，请先确认客户端实际配置目录。");
            return Path.Combine(Path.GetFullPath(custom), "config.toml");
        }

        internal static string PackageFamily(string appRoot)
        {
            string package = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(appRoot))).Name;
            Match match = Regex.Match(package, @"^OpenAI\.Codex_[0-9.]+_[a-z0-9]+_[^_]*_([A-Za-z0-9]+)$");
            if (!match.Success) throw new InvalidOperationException("无法确认官方 Codex 安装包的用户数据目录，未修改缓存。");
            return "OpenAI.Codex_" + match.Groups[1].Value;
        }

        internal static List<DesktopProfile> ResolveAt(string appRoot, string roamingRoot, string localRoot, string customData)
        {
            bool modern = File.Exists(Path.Combine(appRoot, "resources", "owl-electron-app.json"));
            string normal = modern ? Path.Combine(roamingRoot, "Codex", "web", "Codex") : Path.Combine(roamingRoot, "Codex");
            string privateRoaming = Path.Combine(localRoot, "Packages", PackageFamily(appRoot), "LocalCache", "Roaming");
            var result = new List<DesktopProfile>();
            if (!String.IsNullOrWhiteSpace(customData))
            {
                if (!Path.IsPathRooted(customData)) throw new InvalidOperationException("CODEX_ELECTRON_USER_DATA_PATH 不是绝对路径，请先确认客户端缓存目录。");
                string explicitPath = Path.GetFullPath(customData);
                Add(result, explicitPath, "用户指定的客户端数据目录", modern);
                string prefix = Path.GetFullPath(roamingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (explicitPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    Add(result, Path.Combine(privateRoaming, explicitPath.Substring(prefix.Length)), "应用包私有数据目录（已有时处理）", modern);
                return result;
            }
            Add(result, normal, "普通 AppData 数据目录", modern);
            Add(result, modern ? Path.Combine(privateRoaming, "Codex", "web", "Codex") : Path.Combine(privateRoaming, "Codex"), "应用包私有数据目录（已有时处理）", modern);
            return result;
        }

        internal static List<DesktopProfile> WriteTargets(List<DesktopProfile> candidates)
        {
            var result = candidates.FindAll(profile => profile.Initialized);
            // Fresh installations get the normal directory only. A private
            // profile is never created speculatively, nor copied over another.
            if (result.Count == 0) result.Add(candidates[0]);
            return result;
        }

        internal static List<string> LogRootsAt(string appRoot, string localRoot)
        {
            return new List<string> {
                Path.Combine(localRoot, "Codex", "Logs"),
                Path.Combine(localRoot, "Packages", PackageFamily(appRoot), "LocalCache", "Local", "Codex", "Logs")
            };
        }

        private static void Add(List<DesktopProfile> result, string userData, string label, bool modern)
        {
            string full = Path.GetFullPath(userData);
            if (result.Exists(item => String.Equals(item.UserDataDirectory, full, StringComparison.OrdinalIgnoreCase))) return;
            string cache = Path.Combine(modern ? Path.Combine(full, "Default") : full, "Local Storage", "leveldb");
            result.Add(new DesktopProfile {
                UserDataDirectory = full, CacheDirectory = cache, Label = label,
                Initialized = File.Exists(Path.Combine(cache, "CURRENT")) || File.Exists(Path.Combine(full, "statsig-state.json")) || File.Exists(Path.Combine(modern ? Path.Combine(full, "Default") : full, "Preferences"))
            });
        }

        internal static string RuntimeSummary(string appRoot, List<DesktopProfile> candidates)
        {
            // Only whitelisted process metadata is emitted. Never return raw
            // command lines, which can contain prompts, tokens, or other keys.
            string script = @"
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$found = @()
foreach ($p in (Get-CimInstance Win32_Process -Filter ""Name = 'ChatGPT.exe' OR Name = 'Codex.exe'"")) {
    if (!$p.ExecutablePath) { continue }
    if ($p.Name -ieq 'Codex.exe' -and $p.ExecutablePath -notmatch '\\app\\Codex\.exe$') { continue }
    try { $owner = Invoke-CimMethod -InputObject $p -MethodName GetOwnerSid; if ($owner.Sid -ne $sid) { continue } } catch { continue }
    $match = [regex]::Match($p.CommandLine, '--user-data-dir(?:=|\s+)(?:""([^""]+)""|([^\s]+))')
    $dataPath = if ($match.Success) { $match.Groups[1].Value + $match.Groups[2].Value } else { $null }
    $found += [pscustomobject]@{ path = $p.ExecutablePath; pid = [int]$p.ProcessId; dataPath = $dataPath }
}
ConvertTo-Json -InputObject $found -Compress
";
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var start = new ProcessStartInfo(powershell, "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                using (Process process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000)) { try { process.Kill(); } catch { } return "客户端运行目录检查：未完成；未输出原始进程参数。"; }
                    string stdout = output.GetAwaiter().GetResult();
                    error.GetAwaiter().GetResult();
                    if (process.ExitCode != 0) return "客户端运行目录检查：系统查询未完成。";
                    var rows = new JavaScriptSerializer().Deserialize<Dictionary<string, object>[]>(stdout);
                    if (rows == null || rows.Length == 0) return "客户端运行状态：未检测到当前用户运行的桌面进程；请先打开 Codex 再检查中文状态。";
                    var report = new StringBuilder();
                    var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var dataDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in rows)
                    {
                        string executable = row["path"] as string;
                        if (directories.Add(Path.GetDirectoryName(executable)))
                            report.AppendLine("运行中的客户端目录：" + Path.GetDirectoryName(executable) + (String.Equals(Path.GetDirectoryName(executable), appRoot, StringComparison.OrdinalIgnoreCase) ? "（与检测安装目录一致）" : "（与检测安装目录不同）"));
                        string data = row["dataPath"] as string;
                        if (String.IsNullOrWhiteSpace(data) || !Path.IsPathRooted(data) || !dataDirectories.Add(data)) continue;
                        string full = Path.GetFullPath(data);
                        bool known = candidates.Exists(candidate => String.Equals(candidate.UserDataDirectory, full, StringComparison.OrdinalIgnoreCase));
                        report.AppendLine("进程声明的客户端数据目录：" + full + (known ? "（属于已检查候选目录；Windows 仍可能映射到应用包目录）" : "（不属于已检查目录，请核对启动方式或数据目录覆盖设置）"));
                    }
                    if (dataDirectories.Count == 0) report.AppendLine("进程未声明 --user-data-dir；仅凭缓存回读无法确认实际读取位置。");
                    return report.ToString().Trim();
                }
            }
            catch { return "客户端运行目录检查：未完成；未输出原始进程参数。"; }
        }
    }
}
