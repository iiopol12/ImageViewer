using System.Windows;

namespace ImageViewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Handle startup arguments
            if (e.Args.Length > 0)
            {
                var filePath = e.Args[0];
                if (System.IO.File.Exists(filePath))
                {
                    // Store the path to load after window is ready
                    Current.Properties["StartupFile"] = filePath;
                }
            }
        }
    }
}
