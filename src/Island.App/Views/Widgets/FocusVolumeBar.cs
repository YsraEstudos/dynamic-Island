using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Island.App.Views.Widgets;

/// <summary>
/// Thin horizontal volume bar that follows the pointer while it is pressed and dragged. It is drawn with two Borders and
/// no spring, so the fill is always exactly where the pointer is, and reduced motion needs no special case.
/// <see cref="Dragged"/> is raised only for user input; setting <see cref="Value"/> from code does not raise it.
/// </summary>
public sealed class FocusVolumeBar : Grid
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FocusVolumeBar), new PropertyMetadata(0.0, OnValueChanged));

    private static readonly Brush TrackBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));
    private static readonly Brush FillBrush = new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF));

    private readonly Border _fill;
    private bool _dragging;

    public FocusVolumeBar()
    {
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;

        Children.Add(new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = TrackBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });

        _fill = new Border
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = FillBrush,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Children.Add(_fill);

        SizeChanged += (_, _) => Layout();
        MouseLeftButtonDown += OnPressed;
        MouseMove += (_, e) =>
        {
            if (_dragging) SetFromPointer(e);
        };
        MouseLeftButtonUp += OnReleased;
    }

    /// <summary>Fraction 0..1 to show.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0.0, 1.0));
    }

    /// <summary>Raised with the new fraction each time the user moves the value.</summary>
    public event Action<double>? Dragged;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FocusVolumeBar)d).Layout();

    private void Layout() => _fill.Width = Math.Max(0.0, ActualWidth * Value);

    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        SetFromPointer(e);
        e.Handled = true;
    }

    private void OnReleased(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void SetFromPointer(MouseEventArgs e)
    {
        if (ActualWidth <= 0.0) return;

        double fraction = Math.Clamp(e.GetPosition(this).X / ActualWidth, 0.0, 1.0);
        Value = fraction;
        Dragged?.Invoke(fraction);
    }
}
