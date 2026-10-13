using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassIsland.ViewModels;

namespace ClassIsland.Views;

public partial class BashuHomeworkPreviewWindow : Window
{
    private static BashuHomeworkPreviewWindow? _instance;
    private double _expandedHeight = 720;
    public BashuHomeworkPreviewViewModel ViewModel { get; } = new();
    public BashuHomeworkPreviewWindow()
    {
        DataContext = ViewModel;
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        Opened += (_, _) => FitScreen();
    }
    public static void ShowPreview()
    {
        if (_instance == null)
        {
            _instance = new BashuHomeworkPreviewWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        _instance.Activate();
    }
    private void FitScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var scale = screen.Scaling;
        Width = Math.Min(Width, screen.WorkingArea.Width / scale - 24);
        Height = Math.Min(Height, screen.WorkingArea.Height / scale - 24);
        _expandedHeight = Height;
        Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * scale) - 12, screen.WorkingArea.Y + 12);
    }
    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void Fold_OnClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.Expanded) { _expandedHeight = Height; ViewModel.Expanded = false; Height = 124; }
        else { ViewModel.Expanded = true; Height = _expandedHeight; }
    }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
    private void Next_OnClick(object? sender, RoutedEventArgs e) => ViewModel.Next();
    private void Smaller_OnClick(object? sender, RoutedEventArgs e) => ViewModel.TextSize -= 2;
    private void Larger_OnClick(object? sender, RoutedEventArgs e) => ViewModel.TextSize += 2;
}
