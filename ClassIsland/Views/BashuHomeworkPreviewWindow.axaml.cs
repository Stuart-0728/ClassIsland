using System;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClassIsland.ViewModels;

namespace ClassIsland.Views;

public partial class BashuHomeworkPreviewWindow : Window
{
    private static BashuHomeworkPreviewWindow? _instance;
    private static BashuHomeworkPreviewWindow? _liveInstance;
    private static BashuHomeworkSnapshot? _latest;
    private static string? _closedRevision;
    public static void UpdateLive(BashuHomeworkSnapshot snapshot)
    {
        var changed = _latest?.Revision != snapshot.Revision;
        _latest = snapshot;
        if (_liveInstance == null && _closedRevision != snapshot.Revision)
        {
            OpenLive();
            return;
        }
        // Stable snapshots must not rebuild the list and reset the teacher's scroll position.
        if (changed) _liveInstance?.ViewModel.ApplySnapshot(snapshot);
    }
    public static void OpenLive()
    {
        if (_liveInstance == null)
        {
            _liveInstance = new BashuHomeworkPreviewWindow { Title = "巴蜀班级作业 · 平台实时同步" };
            if (_latest != null)
            {
                _liveInstance.ViewModel.ApplySnapshot(_latest);
            }
            else
            {
                _liveInstance.ViewModel.EnterLiveMode();
            }
            _liveInstance.Closed += (_, _) => { _closedRevision = _latest?.Revision; _liveInstance = null; };
            _liveInstance.Show();
        }
        else _liveInstance.Activate();
    }
    public static void ResetLive()
    {
        _liveInstance?.Close(); _liveInstance = null; _latest = null; _closedRevision = null;
    }
    private const double BaseCompactHeight = 68;
    private const double BaseCompactWidth = 380;
    private const double BaseExpandedHeight = 680;
    private const double BaseExpandedWidth = 440;

    private double CompactHeight => BaseCompactHeight * ViewModel.WindowScale;
    private double CompactWidth => BaseCompactWidth * ViewModel.WindowScale;
    private double ExpandedHeight => BaseExpandedHeight * ViewModel.WindowScale;
    private double ExpandedWidth => BaseExpandedWidth * ViewModel.WindowScale;

    private readonly DispatcherTimer _tickerTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _tickerClock = new();
    private double _tickerSeconds;
    public BashuHomeworkPreviewViewModel ViewModel { get; } = new();
    public BashuHomeworkPreviewWindow()
    {
        try
        {
            var mgmt = App.GetService<ClassIsland.Core.Abstractions.Services.Management.IManagementService>();
            if (mgmt?.Settings?.BashuHomeworkWindowScale is > 0.5 and < 2.0)
            {
                ViewModel.WindowScale = mgmt.Settings.BashuHomeworkWindowScale;
            }
        }
        catch { }

        DataContext = ViewModel;
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        if (!ViewModel.Expanded)
        {
            Width = CompactWidth;
            Height = CompactHeight;
            CanResize = false;
            MinHeight = 52;
        }
        _tickerTimer.Tick += (_, _) => TickCompact();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Opened += (_, _) => FitScreen();
        Closed += (_, _) =>
        {
            _tickerTimer.Stop();
            _tickerClock.Stop();
            ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        };
    }
    public static void ShowPreview()
    {
        OpenLive();
    }
    private void FitScreen()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        var scale = screen.Scaling;
        if (!ViewModel.Expanded)
        {
            CanResize = false;
            Width = CompactWidth;
            Height = CompactHeight;
            MinHeight = 52;
        }
        else
        {
            CanResize = true;
            Width = Math.Min(ExpandedWidth, screen.WorkingArea.Width / scale - 24);
            Height = Math.Min(ExpandedHeight, screen.WorkingArea.Height / scale - 24);
        }
        Position = new PixelPoint(screen.WorkingArea.Right - (int)(Width * scale) - 16, screen.WorkingArea.Y + 16);
    }
    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Header buttons must keep their click instead of starting a window drag.
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
    private void Fold_OnClick(object? sender, RoutedEventArgs e)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen?.Scaling ?? 1;
        var right = Position.X + (int)(Width * scale);
        if (ViewModel.Expanded)
        {
            ViewModel.Expanded = false;
            CanResize = false;
            MinHeight = 52;
            Width = CompactWidth;
            Height = CompactHeight;
        }
        else
        {
            ViewModel.Expanded = true;
            CanResize = true;
            MinHeight = 280;
            Width = ExpandedWidth;
            Height = ExpandedHeight;
        }
        // Keep the right edge anchored, including after dragging the folded reminder.
        var x = right - (int)(Width * scale);
        var y = Position.Y;
        if (screen != null)
        {
            Width = Math.Min(Width, screen.WorkingArea.Width / scale - 24);
            Height = Math.Min(Height, screen.WorkingArea.Height / scale - 24);
            x = Math.Clamp(x, screen.WorkingArea.X, Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right - (int)(Width * scale)));
            y = Math.Clamp(y, screen.WorkingArea.Y, Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - (int)(Height * scale)));
        }
        Position = new PixelPoint(x, y);
    }
    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.WindowScale))
        {
            try
            {
                var mgmt = App.GetService<ClassIsland.Core.Abstractions.Services.Management.IManagementService>();
                if (mgmt?.Settings != null)
                {
                    mgmt.Settings.BashuHomeworkWindowScale = ViewModel.WindowScale;
                }
            }
            catch { }
            FitScreen();
            return;
        }
        if (e.PropertyName is not (nameof(ViewModel.Expanded) or nameof(ViewModel.CompactText))) return;
        _tickerSeconds = 0;
        Canvas.SetLeft(this.FindControl<TextBlock>("CompactTicker")!, 0);
        _tickerClock.Restart();
        if (ViewModel.Expanded) _tickerTimer.Stop();
        else _tickerTimer.Start();
    }
    private void TickCompact()
    {
        var elapsed = _tickerClock.Elapsed.TotalSeconds;
        _tickerClock.Restart();
        var viewport = this.FindControl<Canvas>("CompactViewport")!;
        var text = this.FindControl<TextBlock>("CompactTicker")!;
        // Pause under the pointer so names and long assignments remain readable.
        if (ViewModel.Expanded || viewport.IsPointerOver) return;
        _tickerSeconds += Math.Min(elapsed, 0.25);
        var distance = Math.Max(0, text.Bounds.Width - viewport.Bounds.Width);
        const double speed = 28;
        Canvas.SetLeft(text, -Math.Clamp((_tickerSeconds - 2) * speed, 0, distance));
        if (_tickerSeconds >= (distance > 0 ? 4 + distance / speed : 6)) ViewModel.AdvanceCompactCard();
    }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
