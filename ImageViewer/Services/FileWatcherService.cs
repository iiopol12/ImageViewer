using System;
using System.IO;

namespace ImageViewer.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private string? _currentFolder;
        private bool _includeSubdirectories;
        private int _maxSubfolderDepth;
        
        public event EventHandler<FileSystemEventArgs>? FileCreated;
        public event EventHandler<FileSystemEventArgs>? FileDeleted;
        public event EventHandler<RenamedEventArgs>? FileRenamed;
        public event EventHandler<FileSystemEventArgs>? FileChanged;
        
        public void WatchFolder(string folderPath)
        {
            WatchFolder(folderPath, includeSubfolders: false, maxSubfolderDepth: 0);
        }

        public void WatchFolder(string folderPath, bool includeSubfolders, int maxSubfolderDepth)
        {
            StopWatching();
            
            if (!Directory.Exists(folderPath))
                return;
            
            _currentFolder = folderPath;
            _includeSubdirectories = includeSubfolders;
            _maxSubfolderDepth = includeSubfolders ? maxSubfolderDepth : 0;
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = includeSubfolders
            };
            
            _watcher.Created += OnFileCreated;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Changed += OnFileChanged;
        }
        
        public void StopWatching()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Deleted -= OnFileDeleted;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Changed -= OnFileChanged;
                _watcher.Dispose();
                _watcher = null;
            }
            _currentFolder = null;
            _includeSubdirectories = false;
            _maxSubfolderDepth = 0;
        }

        private bool IsWithinDepth(string path)
        {
            if (!_includeSubdirectories)
            {
                return true;
            }

            if (_maxSubfolderDepth < 0)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_currentFolder))
            {
                return false;
            }

            string relative;
            try
            {
                relative = Path.GetRelativePath(_currentFolder, path);
            }
            catch
            {
                return false;
            }

            if (relative.StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            var relativeDir = Path.GetDirectoryName(relative);
            if (string.IsNullOrWhiteSpace(relativeDir) || relativeDir == ".")
            {
                return 0 <= _maxSubfolderDepth;
            }

            var depth = relativeDir.Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                    StringSplitOptions.RemoveEmptyEntries)
                .Length;

            return depth <= _maxSubfolderDepth;
        }
        
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath) && IsWithinDepth(e.FullPath))
            {
                FileCreated?.Invoke(this, e);
            }
        }
        
        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath) && IsWithinDepth(e.FullPath))
            {
                FileDeleted?.Invoke(this, e);
            }
        }
        
        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if ((ImageService.IsSupportedImage(e.FullPath) || ImageService.IsSupportedImage(e.OldFullPath)) &&
                (IsWithinDepth(e.FullPath) || IsWithinDepth(e.OldFullPath)))
            {
                FileRenamed?.Invoke(this, e);
            }
        }
        
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath) && IsWithinDepth(e.FullPath))
            {
                FileChanged?.Invoke(this, e);
            }
        }
        
        public void Dispose()
        {
            StopWatching();
        }
    }
}
