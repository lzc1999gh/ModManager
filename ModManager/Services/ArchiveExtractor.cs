using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ModManager.Services
{
    /// <summary>
    /// 压缩包解压：优先用内置 SharpCompress；
    /// 遇到其解码器不支持的情况（典型：WinRAR 7 用超大字典打包的 RAR，报
    /// “invalid window size”或整数溢出）时，回退到系统解压器（WinRAR / 7-Zip）。
    /// </summary>
    internal static class ArchiveExtractor
    {
        private enum ExternalToolKind { Unrar, SevenZip }

        /// <summary>进程生命周期内缓存探测结果；Path 为空表示探测过但不存在。</summary>
        private static (string Path, ExternalToolKind Kind)? _externalTool;

        /// <summary>解压超时（大压缩包在机械盘上可能要几分钟）。</summary>
        private static readonly int ExtractionTimeoutMs = 20 * 60 * 1000;

        /// <summary>
        /// 把压缩包解压到 destination。整体成败语义：任一环节失败即视为失败，
        /// 由调用方负责清理 destination 中的残留。
        /// </summary>
        public static bool TryExtract(string archivePath, string destination, out string? error)
        {
            error = null;
            if (TryExtractWithSharpCompress(archivePath, destination, out var sharpError)) return true;
            if (TryExtractWithExternalTool(archivePath, destination, out var externalError))
            {
                Debug.WriteLine($"[Archive] '{archivePath}' SharpCompress 失败（{sharpError}），已由系统解压器完成。");
                error = null;
                return true;
            }

            error = externalError ?? sharpError;
            return false;
        }

        private static bool TryExtractWithSharpCompress(string archivePath, string destination, out string? error)
        {
            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    entry.WriteToDirectory(destination, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] SharpCompress 解压失败 '{archivePath}': {ex.Message}");
                error = ex.Message;
                return false;
            }
        }

        private static bool TryExtractWithExternalTool(string archivePath, string destination, out string? error)
        {
            error = null;
            var tool = FindExternalTool();
            if (tool == null)
            {
                error = "内置解压失败，且系统中未找到 WinRAR 或 7-Zip";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool.Value.Path,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.ArgumentList.Add("x");
                psi.ArgumentList.Add("-y");
                if (tool.Value.Kind == ExternalToolKind.Unrar)
                {
                    psi.ArgumentList.Add("-o+");
                    psi.ArgumentList.Add(archivePath);
                    // UnRAR 依赖目标路径末尾的分隔符识别“解压到该目录”
                    psi.ArgumentList.Add(destination + Path.DirectorySeparatorChar);
                }
                else
                {
                    psi.ArgumentList.Add("-o" + destination);
                    psi.ArgumentList.Add(archivePath);
                }

                using var process = Process.Start(psi);
                if (process == null)
                {
                    error = "无法启动系统解压工具";
                    return false;
                }

                // 持续读取输出防止管道写满导致子进程阻塞
                process.OutputDataReceived += static (_, _) => { };
                process.ErrorDataReceived += static (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(ExtractionTimeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { /* 尽力而为 */ }
                    error = "解压超时";
                    return false;
                }

                var producedFiles = Directory.Exists(destination)
                    && Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Any();
                if (process.ExitCode is 0 or 1 && producedFiles) return true; // 1 = 带警告但已完成

                error = $"解压工具退出码 {process.ExitCode}";
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Archive] 系统解压器失败 '{archivePath}': {ex}");
                error = ex.Message;
                return false;
            }
        }

        private static (string Path, ExternalToolKind Kind)? FindExternalTool()
        {
            if (_externalTool != null)
                return string.IsNullOrEmpty(_externalTool.Value.Path) ? null : _externalTool;

            foreach (var directory in EnumerateCandidateDirectories())
            {
                foreach (var name in new[] { "UnRAR.exe", "Rar.exe" })
                {
                    var rarTool = Path.Combine(directory, name);
                    if (File.Exists(rarTool))
                        return _externalTool = (rarTool, ExternalToolKind.Unrar);
                }

                var sevenZip = Path.Combine(directory, "7z.exe");
                if (File.Exists(sevenZip))
                    return _externalTool = (sevenZip, ExternalToolKind.SevenZip);
            }

            _externalTool = (string.Empty, default);
            return null;
        }

        private static System.Collections.Generic.IEnumerable<string> EnumerateCandidateDirectories()
        {
            // 1) 注册表中的 WinRAR 安装信息（便携版装了 shell 扩展也会写 HKLM\SOFTWARE\WinRAR）
            foreach (var directory in WinrarDirectoriesFromRegistry()) yield return directory;

            // 2) .rar 文件关联指向的程序目录
            var associated = WinrarDirectoryFromAssociation();
            if (associated != null) yield return associated;

            // 3) 标准安装位置
            foreach (var programs in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     })
            {
                if (string.IsNullOrEmpty(programs)) continue;
                yield return Path.Combine(programs, "WinRAR");
                yield return Path.Combine(programs, "7-Zip");
            }

            // 4) 注册表中的 7-Zip 安装路径
            var sevenZipDirectory = SevenZipDirectoryFromRegistry();
            if (sevenZipDirectory != null) yield return sevenZipDirectory;

            // 5) PATH 环境变量
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
                foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return dir;
        }

        private static System.Collections.Generic.IEnumerable<string> WinrarDirectoriesFromRegistry()
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                string? winrar = null;
                try
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\WinRAR");
                    winrar = (key?.GetValue("exe64") as string) ?? (key?.GetValue("exe") as string);
                }
                catch { /* 注册表不可读时忽略 */ }

                if (!string.IsNullOrWhiteSpace(winrar))
                    yield return Path.GetDirectoryName(winrar)!;
            }
        }

        private static string? WinrarDirectoryFromAssociation()
        {
            try
            {
                using var assocKey = Registry.ClassesRoot.OpenSubKey(".rar");
                var className = assocKey?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(className)) return null;

                using var cmdKey = Registry.ClassesRoot.OpenSubKey($"{className}\\shell\\open\\command");
                var executable = ParseExecutable(cmdKey?.GetValue(null) as string);
                return string.IsNullOrWhiteSpace(executable) ? null : Path.GetDirectoryName(executable);
            }
            catch
            {
                return null; // 关联缺失时忽略
            }
        }

        private static string? SevenZipDirectoryFromRegistry()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\7-Zip");
                var path = (key?.GetValue("Path64") ?? key?.GetValue("Path")) as string;
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch
            {
                return null; // 忽略
            }
        }

        /// <summary>从关联命令行（如 "D:\...\WinRAR.exe" "%1"）提取可执行文件路径。</summary>
        private static string? ParseExecutable(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            if (command.StartsWith('"'))
            {
                var end = command.IndexOf('"', 1);
                return end > 1 ? command[1..end] : null;
            }

            var space = command.IndexOf(' ');
            return space > 0 ? command[..space] : command;
        }
    }
}
