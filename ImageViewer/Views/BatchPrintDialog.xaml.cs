using ImageViewer.Models;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ImageViewer.Views
{
    public partial class BatchPrintDialog : Window, INotifyPropertyChanged
    {
        public ObservableCollection<BatchPrintItem> Items { get; } = new();

        private int _copies = 1;
        public int Copies
        {
            get => _copies;
            private set
            {
                if (_copies == value)
                {
                    return;
                }

                _copies = value;
                OnPropertyChanged(nameof(Copies));
            }
        }

        public int SelectedCount => Items.Count(item => item.IsSelected);

        public BatchPrintDialog(IReadOnlyList<ImageInfo> images)
        {
            DataContext = this;
            InitializeComponent();

            if (images == null)
            {
                UpdateSelectedCount();
                return;
            }

            foreach (var image in images)
            {
                if (image == null)
                {
                    continue;
                }

                var item = new BatchPrintItem(image);
                item.PropertyChanged += Item_PropertyChanged;
                Items.Add(item);
            }

            UpdateSelectedCount();
        }

        public IReadOnlyList<ImageInfo> GetSelectedImages()
        {
            return Items.Where(item => item.IsSelected)
                        .Select(item => item.Image)
                        .ToList();
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BatchPrintItem.IsSelected))
            {
                UpdateSelectedCount();
            }
        }

        private void UpdateSelectedCount()
        {
            OnPropertyChanged(nameof(SelectedCount));
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items)
            {
                item.IsSelected = true;
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in Items)
            {
                item.IsSelected = false;
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedItem(-1);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedItem(1);
        }

        private void MoveSelectedItem(int direction)
        {
            if (ItemsList.SelectedItem is not BatchPrintItem item)
            {
                return;
            }

            var index = Items.IndexOf(item);
            var newIndex = index + direction;
            if (newIndex < 0 || newIndex >= Items.Count)
            {
                return;
            }

            Items.Move(index, newIndex);
            ItemsList.SelectedItem = item;
        }

        private void Print_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;

            if (SelectedCount == 0)
            {
                ShowError(Services.LanguageManager.GetString("BatchPrint_EmptySelection"));
                return;
            }

            if (!int.TryParse(CopiesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var copies))
            {
                copies = 1;
            }

            copies = Math.Max(1, Math.Min(99, copies));
            Copies = copies;
            CopiesTextBox.Text = copies.ToString(CultureInfo.InvariantCulture);

            DialogResult = true;
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorText.Visibility = Visibility.Visible;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public sealed class BatchPrintItem : INotifyPropertyChanged
        {
            public BatchPrintItem(ImageInfo image)
            {
                Image = image;
                IsSelected = true;
                DisplayName = image.FileName;
                FilePath = image.FilePath;
            }

            public ImageInfo Image { get; }
            public string DisplayName { get; }
            public string FilePath { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value)
                    {
                        return;
                    }

                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }
    }
}
