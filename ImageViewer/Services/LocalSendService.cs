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
        /// 通过 LocalSend 发送文件列表（支持多文件）。
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



        private bool TrySendViaGui(string guiPath, IEnumerable<string> files)
        {
            try
            {
                // GUI版本直接传递文件路径，无需 "file://" 前缀
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

       
    }
}
