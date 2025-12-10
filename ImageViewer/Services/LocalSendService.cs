using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ImageViewer.Services
{
    public class LocalSendService
    {
        public record ShareResult(bool Success, string? ErrorMessage, int SentCount, int SkippedMissing);

        private string? _cachedCliPath;

        /// <summary>
        /// 通过 LocalSend 发送文件列表（支持多文件）。优先使用 CLI，未找到时返回错误提示。
        /// </summary>
        public async Task<ShareResult> SendAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
        {
            var fileList = filePaths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingFiles = fileList.Where(File.Exists).ToList();
            var skippedMissing = fileList.Count - existingFiles.Count;

            if (existingFiles.Count == 0)
            {
                return new ShareResult(false, "没有可发送的文件", 0, skippedMissing);
            }

            var cliPath = FindLocalSendCli();
            if (cliPath != null)
            {
                var cliResult = await SendViaCliAsync(cliPath, existingFiles, skippedMissing, cancellationToken);
                return cliResult;
            }

            // CLI 不可用时，尝试协议唤起 LocalSend GUI
            if (TrySendViaProtocol(existingFiles))
            {
                return new ShareResult(true, null, existingFiles.Count, skippedMissing);
            }

            // 再次尝试直接启动 LocalSend.exe（仅若找到 GUI 路径）
            if (TrySendViaGui(existingFiles))
            {
                return new ShareResult(true, null, existingFiles.Count, skippedMissing);
            }

            return new ShareResult(false, "未找到 LocalSend CLI，且协议/应用启动失败，请确认已安装 LocalSend。", 0, skippedMissing);
        }

        /// <summary>
        /// 尝试寻找 LocalSend CLI 的路径，结果会缓存。
        /// </summary>
        private string? FindLocalSendCli()
        {
            if (!string.IsNullOrWhiteSpace(_cachedCliPath) && File.Exists(_cachedCliPath))
            {
                return _cachedCliPath;
            }

            foreach (var candidate in EnumerateCliCandidates())
            {
                if (File.Exists(candidate))
                {
                    _cachedCliPath = candidate;
                    break;
                }
            }

            return _cachedCliPath;
        }

        private IEnumerable<string> EnumerateCliCandidates()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var known = new[]
            {
                Path.Combine(localAppData, "Programs", "LocalSend", "localsend.exe"),
                Path.Combine(localAppData, "Programs", "LocalSend", "LocalSend.exe"),
                Path.Combine(programFiles, "LocalSend", "localsend.exe"),
                Path.Combine(programFiles, "LocalSend", "LocalSend.exe"),
                Path.Combine(programFilesX86, "LocalSend", "localsend.exe"),
                Path.Combine(programFilesX86, "LocalSend", "LocalSend.exe")
            };

            foreach (var path in known)
            {
                yield return path;
            }

            foreach (var pathDir in EnumeratePathFolders())
            {
                var cliPath = Path.Combine(pathDir, "localsend.exe");
                if (File.Exists(cliPath))
                {
                    yield return cliPath;
                }

                var altCliPath = Path.Combine(pathDir, "LocalSend.exe");
                if (File.Exists(altCliPath))
                {
                    yield return altCliPath;
                }
            }
        }

        private async Task<ShareResult> SendViaCliAsync(string cliPath, List<string> files, int skippedMissing, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(cliPath)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("send");
            foreach (var file in files)
            {
                startInfo.ArgumentList.Add(file);
            }

            using var process = new Process { StartInfo = startInfo };

            if (!process.Start())
            {
                return new ShareResult(false, "无法启动 LocalSend 进程", 0, skippedMissing);
            }

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0)
            {
                return new ShareResult(true, null, files.Count, skippedMissing);
            }

            var error = await process.StandardError.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(error))
            {
                error = await process.StandardOutput.ReadToEndAsync();
            }

            return new ShareResult(false, string.IsNullOrWhiteSpace(error) ? $"LocalSend 退出码 {process.ExitCode}" : error.Trim(), 0, skippedMissing);
        }

        private bool TrySendViaProtocol(IEnumerable<string> files)
        {
            try
            {
                var encodedFiles = string.Join(",", files.Select(f => Uri.EscapeDataString(f)));
                var uri = $"localsend://send?files={encodedFiles}";
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySendViaGui(IEnumerable<string> files)
        {
            var guiPath = FindLocalSendGui();
            if (guiPath == null)
                return false;

            try
            {
                var args = string.Join(" ", files.Select(f => $"\"{f}\""));
                var psi = new ProcessStartInfo(guiPath)
                {
                    UseShellExecute = true,
                    Arguments = args
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string? FindLocalSendGui()
        {
            foreach (var candidate in EnumerateCliCandidates())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private IEnumerable<string> EnumeratePathFolders()
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
                yield break;

            foreach (var part in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;
                yield return part.Trim().Trim('"');
            }
        }
    }
}
