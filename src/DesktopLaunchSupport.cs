using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Text.RegularExpressions;
using System.Xml;
using System.ComponentModel;

namespace DDCodexInstallerHelper
{
    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId, IntPtr items, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
        [PreserveSig] int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appId, IntPtr items, out uint processId);
    }

    internal static class DesktopLaunchSupport
    {
        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IApplicationActivationManager instance);

        [DllImport("ole32.dll", PreserveSig = true)]
        private static extern int CoAllowSetForegroundWindow([MarshalAs(UnmanagedType.IUnknown)] object instance, IntPtr reserved);

        internal static uint Activate(string appId)
        {
            Guid clsid = new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C");
            Guid iid = typeof(IApplicationActivationManager).GUID;
            IApplicationActivationManager manager = null;
            try
            {
                // A short-lived launcher must keep activation arguments in the
                // system COM host, rather than in the launcher's own process.
                Marshal.ThrowExceptionForHR(CoCreateInstance(ref clsid, IntPtr.Zero, 4, ref iid, out manager));
                CoAllowSetForegroundWindow(manager, IntPtr.Zero);
                uint processId;
                int result = manager.ActivateApplication(appId, null, 2, out processId);
                Marshal.ThrowExceptionForHR(result);
                if (processId == 0) throw new InvalidOperationException("系统应用激活未返回客户端进程。");
                return processId;
            }
            finally
            {
                if (manager != null && Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager);
            }
        }

        internal static string Open(InstalledDesktopApp app)
        {
            return OpenAt(app, Activate, StartExecutable);
        }

        internal static string EnvironmentSummary(string appRoot)
        {
            string build = "未知", architecture = "未知", packageArchitecture = "未知";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string number = Convert.ToString(key.GetValue("CurrentBuildNumber"));
                    string revision = Convert.ToString(key.GetValue("UBR"));
                    if (Regex.IsMatch(number, @"^\d+$") && Regex.IsMatch(revision, @"^\d+$")) build = number + "." + revision;
                }
                string value = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
                if (value == "AMD64" || value == "ARM64" || value == "x86") architecture = value;
                if (!String.IsNullOrWhiteSpace(appRoot))
                {
                    var manifest = new XmlDocument();
                    manifest.XmlResolver = null;
                    manifest.Load(Path.Combine(Path.GetDirectoryName(appRoot), "AppxManifest.xml"));
                    XmlNode identity = manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Identity']");
                    string installed = identity.Attributes["ProcessorArchitecture"].Value;
                    if (installed == "x64" || installed == "arm64" || installed == "x86") packageArchitecture = installed;
                }
            }
            catch { /* Missing diagnostics must not alter repair behavior. */ }
            return "Windows Build：" + build + "；系统架构变量：" + architecture + "；客户端包架构：" + packageArchitecture +
                "。桌面入口使用系统应用激活；启动日志见中文自动检查日志。";
        }

        internal static string OpenAt(InstalledDesktopApp app, Func<string, uint> activate, Func<string, uint> startExecutable)
        {
            Exception activationError;
            try
            {
                uint processId = activate(app.AppUserModelId);
                if (processId == 0) throw new InvalidOperationException("系统应用激活未返回客户端进程。");
                return "系统应用激活已接受；客户端进程：" + processId + "。";
            }
            catch (Exception error) { activationError = error; }
            try
            {
                uint processId = startExecutable(app.ExecutablePath);
                if (processId == 0) throw new InvalidOperationException("桌面启动未返回客户端进程。");
                return "系统应用激活未完成（" + FailureCode(activationError) + "）；已通过桌面程序入口启动，进程：" + processId + "。";
            }
            catch (Exception error)
            {
                throw new InvalidOperationException("Codex 未能启动。系统激活 " + FailureCode(activationError) +
                    "；桌面程序入口 " + FailureCode(error) + "。请在助手里点击“检查启动状态”，再“复制日志”；注册异常可点击“修复启动注册”。", error);
            }
        }

        private static string FailureCode(Exception error)
        {
            var native = error as Win32Exception;
            return "HRESULT 0x" + error.HResult.ToString("X8") + (native == null ? "" : "（Win32 " + native.NativeErrorCode + "）");
        }

        private static uint StartExecutable(string executable)
        {
            using (Process process = Process.Start(new ProcessStartInfo
            {
                FileName = executable, UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)
            }))
            {
                if (process == null) throw new InvalidOperationException("桌面启动未返回客户端进程。");
                return (uint)process.Id;
            }
        }
    }
}
