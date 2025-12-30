using ImageViewer.Models;
using ImageViewer.Services;
using ImageViewer.Views;
using System.IO;
using System.Windows;

namespace ImageViewer
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 加载设置并初始化语言
            var settings = AppSettings.Load();
            LanguageManager.Instance.Initialize(settings.Language);

            string? startupFile = null;
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (File.Exists(filePath))
                {
                    startupFile = filePath;
                }
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            if (!string.IsNullOrWhiteSpace(startupFile))
            {
                mainWindow.ShowInTaskbar = false;
                mainWindow.Opacity = 0;

                try
                {
                    await mainWindow.LoadStartupFileAsync(startupFile);
                }
                finally
                {
                    mainWindow.Show();
                    mainWindow.ShowInTaskbar = true;
                    mainWindow.Activate();
                }

                return;
            }

            mainWindow.Show();
        }
    }
}
