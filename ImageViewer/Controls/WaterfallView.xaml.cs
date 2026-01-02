using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageViewer.Models;
using System.Windows.Controls.Primitives;

namespace ImageViewer.Controls
{
    public partial class WaterfallView : UserControl
    {
        private const double SelectionDragThreshold = 4;
        private bool _isSelecting;
        private bool _selectionHasDragged;
        private Point _selectionStart;
        private INotifyCollectionChanged? _collectionChanged;

        public WaterfallView()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(WaterfallView),
                new PropertyMetadata(null, OnItemsSourceChanged));

        public IEnumerable? ItemsSource
        {
            get => (IEnumerable?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(nameof(ZoomLevel), typeof(double), typeof(WaterfallView),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.Register(nameof(IsActive), typeof(bool), typeof(WaterfallView),
                new PropertyMetadata(false));

        public bool IsActive
        {
            get => (bool)GetValue(IsActiveProperty);
            set => SetValue(IsActiveProperty, value);
        }

        public static readonly DependencyProperty ItemClickCommandProperty =
            DependencyProperty.Register(nameof(ItemClickCommand), typeof(ICommand), typeof(WaterfallView),
                new PropertyMetadata(null));

        public ICommand? ItemClickCommand
        {
            get => (ICommand?)GetValue(ItemClickCommandProperty);
            set => SetValue(ItemClickCommandProperty, value);
        }

        private static readonly DependencyPropertyKey SelectedCountPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(SelectedCount), typeof(int), typeof(WaterfallView),
                new PropertyMetadata(0));

        public static readonly DependencyProperty SelectedCountProperty = SelectedCountPropertyKey.DependencyProperty;

        public int SelectedCount
        {
            get => (int)GetValue(SelectedCountProperty);
            private set => SetValue(SelectedCountPropertyKey, value);
        }

        private static readonly DependencyPropertyKey HasSelectionPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(HasSelection), typeof(bool), typeof(WaterfallView),
                new PropertyMetadata(false));

        public static readonly DependencyProperty HasSelectionProperty = HasSelectionPropertyKey.DependencyProperty;

        public bool HasSelection
        {
            get => (bool)GetValue(HasSelectionProperty);
            private set => SetValue(HasSelectionPropertyKey, value);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaterfallView control)
            {
                control.DetachItems(e.OldValue as IEnumerable);
                control.AttachItems(e.NewValue as IEnumerable);
                control.UpdateSelectionState();
            }
        }

        private void AttachItems(IEnumerable? items)
        {
            if (items == null)
                return;

            if (items is INotifyCollectionChanged collectionChanged)
            {
                _collectionChanged = collectionChanged;
                collectionChanged.CollectionChanged += ItemsSource_CollectionChanged;
            }

            foreach (var item in items)
            {
                AttachItem(item);
            }
        }

        private void DetachItems(IEnumerable? items)
        {
            if (items == null)
                return;

            if (_collectionChanged != null)
            {
                _collectionChanged.CollectionChanged -= ItemsSource_CollectionChanged;
                _collectionChanged = null;
            }

            foreach (var item in items)
            {
                DetachItem(item);
            }
        }

        private void ItemsSource_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    DetachItem(item);
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    AttachItem(item);
                }
            }

            UpdateSelectionState();
        }

        private void AttachItem(object? item)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += Item_PropertyChanged;
            }
        }

        private void DetachItem(object? item)
        {
            if (item is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged -= Item_PropertyChanged;
            }
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImageInfo.IsSelected))
            {
                UpdateSelectionState();
            }
        }

        private void UpdateSelectionState()
        {
            int count = 0;
            if (ItemsSource != null)
            {
                count = ItemsSource.OfType<ImageInfo>().Count(i => i.IsSelected);
            }

            SelectedCount = count;
            HasSelection = count > 0;
        }

        public void HandleZoomWithMouseWheel(MouseWheelEventArgs e)
        {
            const double ZOOM_FACTOR = 0.1;
            const double MIN_ZOOM = 0.2;
            const double MAX_ZOOM = 3.0;

            if (!IsActive || ItemsSource == null)
                return;

            Point mousePos = e.GetPosition(WaterfallScrollViewer);

            double horizontalRatio = 0;
            double verticalRatio = 0;

            if (WaterfallScrollViewer.ViewportWidth > 0 && WaterfallScrollViewer.ViewportHeight > 0)
            {
                horizontalRatio = (WaterfallScrollViewer.HorizontalOffset + mousePos.X) / WaterfallScrollViewer.ExtentWidth;
                verticalRatio = (WaterfallScrollViewer.VerticalOffset + mousePos.Y) / WaterfallScrollViewer.ExtentHeight;
            }

            double currentZoom = ZoomLevel;
            double delta = e.Delta > 0 ? ZOOM_FACTOR : -ZOOM_FACTOR;
            double newZoom = currentZoom * (1 + delta);

            newZoom = Math.Max(MIN_ZOOM, Math.Min(MAX_ZOOM, newZoom));

            if (Math.Abs(newZoom - currentZoom) < 0.001)
                return;

            ZoomLevel = newZoom;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (WaterfallScrollViewer.ExtentWidth > 0 && WaterfallScrollViewer.ExtentHeight > 0)
                {
                    double newHorizontalOffset = horizontalRatio * WaterfallScrollViewer.ExtentWidth - mousePos.X;
                    double newVerticalOffset = verticalRatio * WaterfallScrollViewer.ExtentHeight - mousePos.Y;

                    newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, WaterfallScrollViewer.ScrollableWidth));
                    newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, WaterfallScrollViewer.ScrollableHeight));

                    WaterfallScrollViewer.ScrollToHorizontalOffset(newHorizontalOffset);
                    WaterfallScrollViewer.ScrollToVerticalOffset(newVerticalOffset);
                }
            }), DispatcherPriority.Loaded);
        }

        private void WaterfallItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsActive || sender is not FrameworkElement element)
                return;

            if (_isSelecting && _selectionHasDragged)
            {
                e.Handled = true;
                return;
            }

            if (element.Tag is not ImageInfo imageInfo)
                return;

            var modifiers = Keyboard.Modifiers;
            bool ctrlPressed = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shiftPressed = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (ctrlPressed)
            {
                imageInfo.IsSelected = !imageInfo.IsSelected;
                e.Handled = true;
                return;
            }

            if (shiftPressed && imageInfo.IsSelected)
            {
                imageInfo.IsSelected = false;
                e.Handled = true;
                return;
            }

            ClearSelection();

            int index = GetItemIndex(imageInfo);
            if (index >= 0 && ItemClickCommand?.CanExecute(index) == true)
            {
                ItemClickCommand.Execute(index);
            }

            e.Handled = true;
        }

        private int GetItemIndex(ImageInfo imageInfo)
        {
            if (ItemsSource is IList list)
            {
                return list.IndexOf(imageInfo);
            }

            var items = WaterfallItemsControl.Items;
            return items.IndexOf(imageInfo);
        }

        private void WaterfallScrollViewer_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsActive || WaterfallSelectionCanvas == null)
                return;

            var source = e.OriginalSource as DependencyObject;
            if (source == null)
                return;

            if (FindVisualAncestor<ScrollBar>(source) != null ||
                FindVisualAncestor<Thumb>(source) != null ||
                FindVisualAncestor<RepeatButton>(source) != null)
            {
                return;
            }

            var itemContainer = FindVisualAncestor<FrameworkElement>(source, fe => fe.Tag is ImageInfo);
            if (itemContainer != null)
                return;

            _isSelecting = true;
            _selectionHasDragged = false;
            _selectionStart = e.GetPosition(WaterfallSelectionCanvas);

            HideSelectionVisual();
            WaterfallScrollViewer.CaptureMouse();
            e.Handled = true;
        }

        private void WaterfallScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isSelecting || WaterfallSelectionCanvas == null || WaterfallSelectionBorder == null)
                return;

            var currentPoint = e.GetPosition(WaterfallSelectionCanvas);
            var delta = currentPoint - _selectionStart;
            if (!_selectionHasDragged)
            {
                if (Math.Abs(delta.X) < SelectionDragThreshold && Math.Abs(delta.Y) < SelectionDragThreshold)
                {
                    return;
                }

                _selectionHasDragged = true;
                WaterfallSelectionBorder.Visibility = Visibility.Visible;
            }

            var selectionRect = GetSelectionRect(_selectionStart, currentPoint);
            UpdateSelectionVisual(selectionRect);
            e.Handled = true;
        }

        private void WaterfallScrollViewer_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isSelecting || WaterfallSelectionCanvas == null)
                return;

            if (WaterfallScrollViewer.IsMouseCaptured)
            {
                WaterfallScrollViewer.ReleaseMouseCapture();
            }

            var endPoint = e.GetPosition(WaterfallSelectionCanvas);
            var selectionRect = GetSelectionRect(_selectionStart, endPoint);

            HideSelectionVisual();

            if (_selectionHasDragged)
            {
                ToggleSelection(selectionRect);
            }
            else
            {
                ClearSelection();
            }

            _isSelecting = false;
            _selectionHasDragged = false;
            e.Handled = true;
        }

        private void ClearSelection()
        {
            if (ItemsSource == null)
                return;

            foreach (var img in ItemsSource.OfType<ImageInfo>())
            {
                if (img.IsSelected)
                {
                    img.IsSelected = false;
                }
            }

            UpdateSelectionState();
        }

        private Rect GetSelectionRect(Point start, Point end)
        {
            double x1 = Math.Min(start.X, end.X);
            double y1 = Math.Min(start.Y, end.Y);
            double x2 = Math.Max(start.X, end.X);
            double y2 = Math.Max(start.Y, end.Y);

            if (WaterfallSelectionCanvas != null &&
                WaterfallSelectionCanvas.ActualWidth > 0 &&
                WaterfallSelectionCanvas.ActualHeight > 0)
            {
                double maxX = WaterfallSelectionCanvas.ActualWidth;
                double maxY = WaterfallSelectionCanvas.ActualHeight;
                x1 = Math.Max(0, Math.Min(x1, maxX));
                y1 = Math.Max(0, Math.Min(y1, maxY));
                x2 = Math.Max(0, Math.Min(x2, maxX));
                y2 = Math.Max(0, Math.Min(y2, maxY));
            }

            return new Rect(new Point(x1, y1), new Point(x2, y2));
        }

        private void UpdateSelectionVisual(Rect rect)
        {
            if (WaterfallSelectionBorder == null)
                return;

            Canvas.SetLeft(WaterfallSelectionBorder, rect.X);
            Canvas.SetTop(WaterfallSelectionBorder, rect.Y);
            WaterfallSelectionBorder.Width = rect.Width;
            WaterfallSelectionBorder.Height = rect.Height;
        }

        private void HideSelectionVisual()
        {
            if (WaterfallSelectionBorder == null)
                return;

            WaterfallSelectionBorder.Visibility = Visibility.Collapsed;
            WaterfallSelectionBorder.Width = 0;
            WaterfallSelectionBorder.Height = 0;
        }

        private void ToggleSelection(Rect selectionRect)
        {
            if (selectionRect.Width <= 0 || selectionRect.Height <= 0 || WaterfallItemsControl == null ||
                WaterfallSelectionCanvas == null)
            {
                return;
            }

            for (int i = 0; i < WaterfallItemsControl.Items.Count; i++)
            {
                if (WaterfallItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container ||
                    !container.IsVisible ||
                    container.RenderSize.Width <= 0 ||
                    container.RenderSize.Height <= 0)
                {
                    continue;
                }

                var itemRect = container.TransformToVisual(WaterfallSelectionCanvas)
                    .TransformBounds(new Rect(new Point(0, 0), container.RenderSize));

                if (selectionRect.IntersectsWith(itemRect))
                {
                    if (WaterfallItemsControl.Items[i] is ImageInfo imageInfo)
                    {
                        imageInfo.IsSelected = !imageInfo.IsSelected;
                    }
                }
            }

            UpdateSelectionState();
        }

        private static T? FindVisualAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            var current = source;
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static FrameworkElement? FindVisualAncestor<FrameworkElement>(DependencyObject source, Func<FrameworkElement, bool> predicate)
            where FrameworkElement : System.Windows.FrameworkElement
        {
            var current = source;
            while (current != null)
            {
                if (current is System.Windows.FrameworkElement fe && predicate((FrameworkElement)fe))
                    return (FrameworkElement)fe;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
