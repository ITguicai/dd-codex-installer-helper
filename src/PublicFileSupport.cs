using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace DDCodexInstallerHelper
{
    internal static class PublicFileSupport
    {
        internal static bool SameContents(string source, string destination)
        {
            if (!File.Exists(destination)) return false;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (input.Length != existing.Length) return false;
                using (var hash = SHA256.Create())
                {
                    string expected = Convert.ToBase64String(hash.ComputeHash(input));
                    return expected == Convert.ToBase64String(hash.ComputeHash(existing));
                }
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool MoveFileEx(string existing, string destination, uint flags);

        // Only for public program resources, never credentials, config or caches.
        // File.Copy propagates source EFS metadata, which can fail with error 6000
        // even when the resource's bytes are readable by the current user.
        internal static void Copy(string source, string destination, bool overwrite)
        {
            string temporary = null;
            string stage = "读取源文件";
            try
            {
                source = Path.GetFullPath(source);
                destination = Path.GetFullPath(destination);
                if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("源文件与目标文件相同。");
                if (!overwrite && File.Exists(destination)) throw new IOException("目标文件已存在。");
                string digest;
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var hash = SHA256.Create())
                {
                    stage = "创建本机副本";
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    temporary = Path.Combine(Path.GetDirectoryName(destination), ".dd-public-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[81920];
                        int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            output.Write(buffer, 0, count);
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        hash.TransformFinalBlock(new byte[0], 0, 0);
                        digest = Convert.ToBase64String(hash.Hash);
                        output.Flush(true);
                    }
                }
                stage = "校验本机副本";
                using (var input = File.OpenRead(temporary))
                using (var hash = SHA256.Create())
                    if (Convert.ToBase64String(hash.ComputeHash(input)) != digest)
                        throw new IOException("公开资源 SHA256 校验不一致。");
                stage = "保存本机副本";
                // The staging file is in the destination directory: rename on
                // the same volume without copying the source's EFS/ACL/ADS.
                if (!MoveFileEx(temporary, destination, (overwrite ? 1u : 0u) | 8u))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                temporary = null;
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is Win32Exception)) throw;
                var native = error as Win32Exception;
                int code = native != null ? native.NativeErrorCode : error.HResult & 0xffff;
                throw new IOException("公开资源复制未完成（" + stage + "；文件 " + Path.GetFileName(source) + "；Windows 错误 " + code + "）。\n目标：" + destination + "\n" + error.Message, error);
            }
            finally
            {
                if (temporary != null)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch { /* Preserve the original failure. */ }
                }
            }
        }
    }
}
