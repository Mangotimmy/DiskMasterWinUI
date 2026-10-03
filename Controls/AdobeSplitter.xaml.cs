using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DiskMasterWinUI.Controls;

public enum AdobeSplitterOrientation
{
    Vertical,   // Left-right resize (↔ cursor, controls ColumnDefinition)
    Horizontal  // Top-bottom resize (↕ cursor, controls RowDefinition)
}

public enum AdobeSplitterResizeMode
{
    TargetFirst,  // Dragging right/down increases target size
    TargetSecond  // Dragging left/up increases target size
}

public sealed partial class AdobeSplitter : UserControl
{
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(AdobeSplitterOrientation), typeof(AdobeSplitter),
            new PropertyMetadata(AdobeSplitterOrientation.Vertical, OnOrientationChanged));

    public static readonly DependencyProperty ResizeModeProperty =
        DependencyProperty.Register(nameof(ResizeMode), typeof(AdobeSplitterResizeMode), typeof(AdobeSplitter),
            new PropertyMetadata(AdobeSplitterResizeMode.TargetFirst));

    public static readonly DependencyProperty MinSizeProperty =
        DependencyProperty.Register(nameof(MinSize), typeof(double), typeof(AdobeSplitter),
            new PropertyMetadata(120.0));

    public static readonly DependencyProperty MaxSizeProperty =
        DependencyProperty.Register(nameof(MaxSize), typeof(double), typeof(AdobeSplitter),
            new PropertyMetadata(double.PositiveInfinity));

    public AdobeSplitterOrientation Orientation
    {
        get => (AdobeSplitterOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public AdobeSplitterResizeMode ResizeMode
    {
        get => (AdobeSplitterResizeMode)GetValue(ResizeModeProperty);
        set => SetValue(ResizeModeProperty, value);
    }

    public double MinSize
    {
        get => (double)GetValue(MinSizeProperty);
        set => SetValue(MinSizeProperty, value);
    }

    public double MaxSize
    {
        get => (double)GetValue(MaxSizeProperty);
        set => SetValue(MaxSizeProperty, value);
    }

    // Direct reference to the ColumnDefinition or RowDefinition to resize
    public ColumnDefinition? TargetColumn { get; set; }
    public RowDefinition? TargetRow { get; set; }

    private bool _isDragging;
    private Point _startPoint;
    private double _startSize;
    private double _savedExpandedSize = 280.0;

    public AdobeSplitter()
    {
        InitializeComponent();
        Loaded += AdobeSplitter_Loaded;
    }

    private void AdobeSplitter_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyOrientationStyles();
    }

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AdobeSplitter splitter)
        {
            splitter.ApplyOrientationStyles();
        }
    }

    private void ApplyOrientationStyles()
    {
        if (Orientation == AdobeSplitterOrientation.Vertical)
        {
            Width = 8;
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Stretch;
            if (VisualLine != null)
            {
                VisualLine.Width = 2;
                VisualLine.HorizontalAlignment = HorizontalAlignment.Center;
                VisualLine.VerticalAlignment = VerticalAlignment.Stretch;
            }
            if (GripIndicator != null)
            {
                GripIndicator.Orientation = Microsoft.UI.Xaml.Controls.Orientation.Vertical;
            }
            try
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
            }
            catch { }
        }
        else
        {
            Height = 8;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Center;
            if (VisualLine != null)
            {
                VisualLine.Height = 2;
                VisualLine.HorizontalAlignment = HorizontalAlignment.Stretch;
                VisualLine.VerticalAlignment = VerticalAlignment.Center;
            }
            if (GripIndicator != null)
            {
                GripIndicator.Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal;
            }
            try
            {
                ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
            }
            catch { }
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Highlight(true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            Highlight(false);
        }
    }

    private void Highlight(bool active)
    {
        if (VisualLine == null) return;

        if (active)
        {
            if (Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var brushObj) && brushObj is Brush brush)
            {
                VisualLine.Background = brush;
            }
            if (Orientation == AdobeSplitterOrientation.Vertical)
                VisualLine.Width = 4;
            else
                VisualLine.Height = 4;
        }
        else
        {
            if (Application.Current.Resources.TryGetValue("CardStrokeColorDefaultBrush", out var strokeObj) && strokeObj is Brush stroke)
            {
                VisualLine.Background = stroke;
            }
            if (Orientation == AdobeSplitterOrientation.Vertical)
                VisualLine.Width = 2;
            else
                VisualLine.Height = 2;
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var parentGrid = Parent as UIElement;
        if (parentGrid == null) return;

        _isDragging = true;
        _startPoint = e.GetCurrentPoint(parentGrid).Position;

        if (Orientation == AdobeSplitterOrientation.Vertical && TargetColumn != null)
        {
            _startSize = TargetColumn.ActualWidth > 0 ? TargetColumn.ActualWidth : TargetColumn.Width.Value;
        }
        else if (Orientation == AdobeSplitterOrientation.Horizontal && TargetRow != null)
        {
            _startSize = TargetRow.ActualHeight > 0 ? TargetRow.ActualHeight : TargetRow.Height.Value;
        }

        CapturePointer(e.Pointer);
        Highlight(true);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        var parentGrid = Parent as UIElement;
        if (parentGrid == null) return;

        var currentPoint = e.GetCurrentPoint(parentGrid).Position;

        if (Orientation == AdobeSplitterOrientation.Vertical && TargetColumn != null)
        {
            var deltaX = currentPoint.X - _startPoint.X;
            var newWidth = ResizeMode == AdobeSplitterResizeMode.TargetFirst
                ? _startSize + deltaX
                : _startSize - deltaX;

            newWidth = Math.Clamp(newWidth, MinSize, MaxSize);
            TargetColumn.Width = new GridLength(newWidth, GridUnitType.Pixel);
        }
        else if (Orientation == AdobeSplitterOrientation.Horizontal && TargetRow != null)
        {
            var deltaY = currentPoint.Y - _startPoint.Y;
            var newHeight = ResizeMode == AdobeSplitterResizeMode.TargetFirst
                ? _startSize + deltaY
                : _startSize - deltaY;

            newHeight = Math.Clamp(newHeight, MinSize, MaxSize);
            TargetRow.Height = new GridLength(newHeight, GridUnitType.Pixel);
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndDrag(e);
    }

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleasePointerCapture(e.Pointer);
            Highlight(false);
            e.Handled = true;
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Orientation == AdobeSplitterOrientation.Vertical && TargetColumn != null)
        {
            if (TargetColumn.ActualWidth <= MinSize + 15)
            {
                TargetColumn.Width = new GridLength(_savedExpandedSize, GridUnitType.Pixel);
            }
            else
            {
                _savedExpandedSize = TargetColumn.ActualWidth;
                TargetColumn.Width = new GridLength(MinSize, GridUnitType.Pixel);
            }
        }
        else if (Orientation == AdobeSplitterOrientation.Horizontal && TargetRow != null)
        {
            if (TargetRow.ActualHeight <= MinSize + 15)
            {
                TargetRow.Height = new GridLength(_savedExpandedSize, GridUnitType.Pixel);
            }
            else
            {
                _savedExpandedSize = TargetRow.ActualHeight;
                TargetRow.Height = new GridLength(MinSize, GridUnitType.Pixel);
            }
        }
        e.Handled = true;
    }
}
