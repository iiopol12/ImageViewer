using System;
using System.IO;

namespace ImageViewer.Services
{
    public class FileWatcherService : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private string? _currentFolder;
        
        public event EventHandler<FileSystemEventArgs>? FileCreated;
        public event EventHandler<FileSystemEventArgs>? FileDeleted;
        public event EventHandler<RenamedEventArgs>? FileRenamed;
        public event EventHandler<FileSystemEventArgs>? FileChanged;
        
        public void WatchFolder(string folderPath)
        {
            StopWatching();
            
            if (!Directory.Exists(folderPath))
                return;
            
            _currentFolder = folderPath;
            _watcher = new FileSystemWatcher(folderPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
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
        }
        
        private void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath))
            {
                FileCreated?.Invoke(this, e);
            }
        }
        
        private void OnFileDeleted(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath))
            {
                FileDeleted?.Invoke(this, e);
            }
        }
        
        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath) || ImageService.IsSupportedImage(e.OldFullPath))
            {
                FileRenamed?.Invoke(this, e);
            }
        }
        
        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (ImageService.IsSupportedImage(e.FullPath))
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
