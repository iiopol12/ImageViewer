using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ImageViewer.Models;

namespace ImageViewer.Services
{
    public class LocalSendService
    {
        public record ShareResult(bool Success, string? ErrorMessage, int SentCount, int SkippedMissing);

        private string? _cachedCliPath;
        private readonly AppSettings? _settings;

        public LocalSendService(AppSettings? settings = null)
        {
            _settings = settings;
        }

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

          
            var preferredPath = _settings?.LocalSendPath;

            // GUI
            var guiPath = ResolveGuiPath(preferredPath);
            if (!string.IsNullOrWhiteSpace(guiPath) && TrySendViaGui(guiPath, existingFiles))
            {
                return new ShareResult(true, null, existingFiles.Count, skippedMissing);
            }

            return new ShareResult(false, "未找到 LocalSend GUI/协议，可在设置中指定路径或安装 LocalSend。", 0, skippedMissing);
        }

        private static bool IsCliExecutable(string path)
        {
            var file = Path.GetFileName(path);
            return file.Equals("localsend.exe", StringComparison.OrdinalIgnoreCase);
        }

        private string? ResolveCliPath(string? preferredPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath) && IsCliExecutable(preferredPath))
            {
                _cachedCliPath = preferredPath;
                return preferredPath;
            }

            if (!string.IsNullOrWhiteSpace(_cachedCliPath) && File.Exists(_cachedCliPath))
            {
                return _cachedCliPath;
            }

            // 自动探测
            foreach (var candidate in EnumerateCliCandidates())
            {
                if (File.Exists(candidate) && IsCliExecutable(candidate))
                {
                    _cachedCliPath = candidate;
                    break;
                }
            }

            return _cachedCliPath;
        }

        private string? ResolveGuiPath(string? preferredPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            {
                return preferredPath;
            }

            foreach (var candidate in EnumerateCliCandidates())
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerable<string> EnumerateCliCandidates()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var known = new[]
            {
                Path.Combine(localAppData, "Programs", "LocalSend", "localsend_app.exe"),
                Path.Combine(programFiles, "LocalSend", "localsend_app.exe"),
                Path.Combine(programFilesX86, "LocalSend", "localsend_app.exe")
            };

            foreach (var path in known)
            {
                yield return path;
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

        private bool TrySendViaGui(string guiPath, IEnumerable<string> files)
        {
            try
            {
                // 关键修正：GUI版本直接传递文件路径，无需 "file://" 前缀
                // 使用 ArgumentList 可以安全处理路径中的空格和特殊字符
                var startInfo = new ProcessStartInfo(guiPath)
                {
                    UseShellExecute = true
                };

                // 先加隐藏启动参数
                //startInfo.ArgumentList.Add("--hidden");

                foreach (var file in files)
                {
                    startInfo.ArgumentList.Add(file); // 直接添加路径，如: "C:\Users\test.png"
                }

                Process.Start(startInfo);
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
