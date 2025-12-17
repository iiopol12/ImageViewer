using System;
using System.IO;
using System.Windows;

namespace ImageViewer.Views
{
    public partial class ArchivePasswordDialog : Window
    {
        public string ArchivePath { get; }

        public string ArchiveDisplayName => Path.GetFileName(ArchivePath);

        public string Password => PasswordBox.Password ?? string.Empty;

        public ArchivePasswordDialog(string archivePath, string? errorMessage = null)
        {
            ArchivePath = archivePath ?? string.Empty;
            InitializeComponent();
            DataContext = this;

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                ErrorText.Text = errorMessage;
                ErrorText.Visibility = Visibility.Visible;
            }

            Loaded += (_, _) => PasswordBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

