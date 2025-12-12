using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Views
{
    public partial class FileAssociationsWindow : Window
    {
        public ObservableCollection<AssociationOption> Options { get; }

        public FileAssociationsWindow(IEnumerable<string> extensions, IEnumerable<string>? preselected = null)
        {
            InitializeComponent();

            var selectedSet = preselected != null
                ? new HashSet<string>(preselected, StringComparer.OrdinalIgnoreCase)
                : null;

            Options = new ObservableCollection<AssociationOption>(
                extensions.Select(ext => new AssociationOption(ext, selectedSet == null || selectedSet.Contains(ext))));

            DataContext = this;
        }

        public IReadOnlyList<string> SelectedExtensions =>
            Options.Where(o => o.IsSelected).Select(o => o.Extension).ToList();

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedExtensions.Count == 0)
            {
                MessageBox.Show("请至少选择一种格式。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var opt in Options)
            {
                opt.IsSelected = true;
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var opt in Options)
            {
                opt.IsSelected = false;
            }
        }

        public sealed partial class AssociationOption : ObservableObject
        {
            public string Extension { get; }
            public string DisplayName { get; }

            [ObservableProperty]
            private bool _isSelected;

            public AssociationOption(string extension, bool isSelected)
            {
                Extension = extension;
                DisplayName = extension.TrimStart('.').ToUpperInvariant();
                _isSelected = isSelected;
            }
        }
    }
}

