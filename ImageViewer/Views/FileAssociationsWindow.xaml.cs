using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageViewer.Views
{
    public partial class FileAssociationsControl : UserControl
    {
        public ObservableCollection<AssociationOption> Options { get; } = new();

        public FileAssociationsControl()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void InitializeExtensions(IEnumerable<string> extensions, IEnumerable<string>? preselected = null)
        {
            Options.Clear();

            var selectedSet = preselected != null
                ? new HashSet<string>(preselected, StringComparer.OrdinalIgnoreCase)
                : null;

            foreach (var ext in extensions)
            {
                Options.Add(new AssociationOption(ext, selectedSet == null || selectedSet.Contains(ext)));
            }
        }

        public IReadOnlyList<string> SelectedExtensions =>
            Options.Where(o => o.IsSelected).Select(o => o.Extension).ToList();

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
