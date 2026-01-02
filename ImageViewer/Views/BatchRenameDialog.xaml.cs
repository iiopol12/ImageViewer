using ImageViewer.Models;
using ImageViewer.Services;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ImageViewer.Views
{
    public partial class BatchRenameDialog : Window
    {
        private readonly string _sampleExtension;

        public string Prefix { get; private set; } = string.Empty;
        public int StartNumber { get; private set; } = 1;
        public int NumberPadding { get; private set; } = 3;

        public BatchRenameDialog(IReadOnlyList<ImageInfo> images)
        {
            InitializeComponent();

            var count = images?.Count ?? 0;
            SelectedCountRun.Text = count.ToString(CultureInfo.InvariantCulture);
            _sampleExtension = GetSampleExtension(images);

            PrefixTextBox.Text = string.Empty;
            StartNumberTextBox.Text = "1";
            PaddingTextBox.Text = "3";
            UpdatePreview();

            PrefixTextBox.Focus();
            PrefixTextBox.SelectAll();
        }

        private static string GetSampleExtension(IReadOnlyList<ImageInfo>? images)
        {
            var first = images?.FirstOrDefault();
            if (first == null || string.IsNullOrWhiteSpace(first.FilePath))
            {
                return string.Empty;
            }

            return Path.GetExtension(first.FilePath);
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var prefix = PrefixTextBox.Text ?? string.Empty;
            var startNumber = ParseIntOrDefault(StartNumberTextBox.Text, 1);
            var padding = ParseIntOrDefault(PaddingTextBox.Text, 3);
            var numberText = padding > 0
                ? startNumber.ToString("D" + padding, CultureInfo.InvariantCulture)
                : startNumber.ToString(CultureInfo.InvariantCulture);

            PreviewTextBlock.Text = $"{prefix}{numberText}{_sampleExtension}";
        }

        private static int ParseIntOrDefault(string? input, int defaultValue)
        {
            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            var prefix = PrefixTextBox.Text ?? string.Empty;
            if (ContainsInvalidFileNameChars(prefix))
            {
                ShowError(LanguageManager.GetString("BatchRename_Error_InvalidPrefix"));
                return;
            }

            if (!int.TryParse(StartNumberTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startNumber) ||
                startNumber < 0)
            {
                ShowError(LanguageManager.GetString("BatchRename_Error_InvalidStartNumber"));
                return;
            }

            if (!int.TryParse(PaddingTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var padding) ||
                padding < 0 ||
                padding > 8)
            {
                ShowError(LanguageManager.GetString("BatchRename_Error_InvalidDigits"));
                return;
            }

            Prefix = prefix;
            StartNumber = startNumber;
            NumberPadding = padding;

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        private static bool ContainsInvalidFileNameChars(string text)
        {
            return text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        }
    }
}
