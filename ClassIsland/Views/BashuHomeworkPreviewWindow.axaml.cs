using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ClassIsland.Core;
using ClassIsland.Models;
using ClassIsland.Shared.Helpers;
using System.Diagnostics;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using System.Globalization;
using System.Threading.Tasks;
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
    private readonly string _statePath;
    private readonly BashuHomeworkWindowState _windowState;
    private bool _isOpened;
    private bool _isClosing;
    private bool _applyingDisplaySettings;
    private IPointer? _dragPointer;
    private PixelPoint _dragStartPointer;
    private PixelPoint _dragStartPosition;
    private bool _dragMoved;

    private readonly DispatcherTimer _tickerTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _tickerClock = new();
    private double _tickerSeconds;
    public BashuHomeworkPreviewViewModel ViewModel { get; } = new();
    public BashuHomeworkPreviewWindow() : this(Path.Combine(CommonDirectories.AppDataFolderPath, "BashuHomeworkWindow.json"))
    {
    }

    internal BashuHomeworkPreviewWindow(string statePath)
    {
        _statePath = statePath;
        _windowState = File.Exists(statePath)
            ? ConfigureFileHelper.LoadConfig<BashuHomeworkWindowState>(statePath)
            : new BashuHomeworkWindowState();
        if (!File.Exists(statePath))
        {
            try
            {
                _windowState.WindowScale = App.GetService<ClassIsland.Core.Abstractions.Services.Management.IManagementService>()
                    .Settings.BashuHomeworkWindowScale;
            }
            catch { /* Standalone previews do not load classroom management services. */ }
        }
        _windowState.Normalize();
        ViewModel.WindowScale = _windowState.WindowScale;
        ViewModel.CompactWidth = _windowState.CompactWidth;
        ViewModel.IsPinned = _windowState.IsPinned;
        DataContext = ViewModel;
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        _tickerTimer.Tick += (_, _) => TickCompact();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        PointerMoved += Window_OnPointerMoved;
        PointerReleased += Window_OnPointerReleased;
        PointerCaptureLost += Window_OnPointerCaptureLost;
        Opened += (_, _) =>
        {
            FitScreen(true);
            _isOpened = true;
            _tickerClock.Restart();
            _tickerTimer.Start();
        };
        Screens.Changed += Screens_OnChanged;
        Closing += (_, _) =>
        {
            _isClosing = true;
            FinishDrag();
        };
        Closed += (_, _) =>
        {
            SaveWindowState();
            _tickerTimer.Stop();
            _tickerClock.Stop();
            Screens.Changed -= Screens_OnChanged;
            ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        };
    }
    public static void ShowPreview() => OpenLive();

    private Avalonia.Platform.Screen? SelectScreen(bool restore)
    {
        if (restore && _windowState.HasPosition)
        {
            return Screens.All.FirstOrDefault(x => !string.IsNullOrEmpty(_windowState.ScreenName) && x.DisplayName == _windowState.ScreenName)
                ?? Screens.All.FirstOrDefault(x => x.WorkingArea.X == _windowState.ScreenX && x.WorkingArea.Y == _windowState.ScreenY)
                ?? Screens.Primary;
        }
        return Screens.ScreenFromWindow(this) ?? Screens.Primary;
    }

    private void FitScreen(bool restore = false)
    {
        var screen = SelectScreen(restore);
        if (screen == null) return;
        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        _windowState.WindowScale = ViewModel.WindowScale;
        _windowState.CompactWidth = ViewModel.CompactWidth;
        var (width, height) = _windowState.GetSize(ViewModel.Expanded, area.Width / scale, area.Height / scale);
        MinWidth = Math.Min(320, Math.Max(1, area.Width / scale - 24));
        MinHeight = ViewModel.Expanded ? Math.Min(280, Math.Max(1, area.Height / scale - 24)) : 1;
        CanResize = ViewModel.Expanded;
        Width = width;
        Height = height;
        var (left, top) = _windowState.GetPosition(width, height, area.Width / scale, area.Height / scale);
        Position = new PixelPoint(area.X + (int)Math.Round(left * scale), area.Y + (int)Math.Round(top * scale));
    }

    private void Screens_OnChanged(object? sender, EventArgs e)
    {
        if (_isOpened && !_isClosing) FitScreen(true);
    }

    private void SaveWindowState()
    {
        if (!_isOpened) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            _windowState.IsPinned = ViewModel.IsPinned;
            _windowState.WindowScale = ViewModel.WindowScale;
            _windowState.CompactWidth = ViewModel.CompactWidth;
            ConfigureFileHelper.SaveConfig(_statePath, _windowState);
        }
        catch (Exception ex) { Trace.TraceWarning("Unable to save homework window placement: {0}", ex.Message); }
    }

    private void Header_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_dragPointer != null) return;
        if (e.Source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() != null)) return;
        if (e.Pointer.Type == PointerType.Mouse && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragPointer = e.Pointer;
        _dragStartPointer = this.PointToScreen(e.GetPosition(this));
        _dragStartPosition = Position;
        _dragMoved = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void Window_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Pointer != _dragPointer) return;
        var point = this.PointToScreen(e.GetPosition(this));
        var dx = point.X - _dragStartPointer.X;
        var dy = point.Y - _dragStartPointer.Y;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 4 * RenderScaling) return;
        _dragMoved = true;
        Position = new PixelPoint(_dragStartPosition.X + dx, _dragStartPosition.Y + dy);
        e.Handled = true;
    }

    private void Window_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Pointer != _dragPointer) return;
        FinishDrag();
        e.Handled = true;
    }

    private void Window_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (e.Pointer == _dragPointer) FinishDrag();
    }

    private void FinishDrag()
    {
        var pointer = _dragPointer;
        if (pointer == null) return;
        _dragPointer = null;
        pointer.Capture(null);
        if (!_dragMoved) return;
        _dragMoved = false;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen == null) return;
        _windowState.ScreenName = screen.DisplayName;
        _windowState.ScreenX = screen.WorkingArea.X;
        _windowState.ScreenY = screen.WorkingArea.Y;
        _windowState.HasPosition = true;
        _windowState.RightInset = Math.Max(0, (screen.WorkingArea.Right - Position.X) / screen.Scaling - Width);
        _windowState.TopInset = Math.Max(0, (Position.Y - screen.WorkingArea.Y) / screen.Scaling);
        FitScreen();
        SaveWindowState();
    }

    private void Fold_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.Expanded = !ViewModel.Expanded;
        FitScreen();
    }

    private void Pin_OnClick(object? sender, RoutedEventArgs e) => ViewModel.IsPinned = !ViewModel.IsPinned;

    private void DisplaySettings_OnOpened(object? sender, EventArgs e)
    {
        ViewModel.DraftWindowScale = ViewModel.WindowScale;
        ViewModel.DraftCompactWidth = ViewModel.CompactWidth;
    }

    private void DisplaySettings_OnClosed(object? sender, EventArgs e)
    {
        _applyingDisplaySettings = true;
        try
        {
            ViewModel.WindowScale = ViewModel.DraftWindowScale;
            ViewModel.CompactWidth = ViewModel.DraftCompactWidth;
        }
        finally { _applyingDisplaySettings = false; }
        if (_isOpened && !_isClosing) FitScreen();
        SaveWindowState();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.IsPinned))
        {
            SaveWindowState();
            return;
        }
        if (e.PropertyName is nameof(ViewModel.WindowScale) or nameof(ViewModel.CompactWidth))
        {
            if (_applyingDisplaySettings) return;
            if (_isOpened) FitScreen();
            SaveWindowState();
            return;
        }
        if (e.PropertyName is not (nameof(ViewModel.Expanded) or nameof(ViewModel.CompactText))) return;
        _tickerSeconds = 0;
        Canvas.SetLeft(this.FindControl<TextBlock>("CompactTicker")!, 0);
        foreach (var heading in this.GetVisualDescendants().OfType<ScrollViewer>().Where(x => x.Classes.Contains("homework-heading")))
            heading.Offset = default;
        _tickerClock.Restart();
        if (_isOpened && !_isClosing) _tickerTimer.Start();
    }
    private void TickCompact()
    {
        var elapsed = _tickerClock.Elapsed.TotalSeconds;
        _tickerClock.Restart();
        AdvanceTicker(elapsed);
    }

    internal void AdvanceTicker(double elapsed)
    {
        if (_isClosing || _isSwitchingCard) return;
        var headings = this.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(x => x.Classes.Contains("homework-heading") && x.IsEffectivelyVisible && x.Viewport.Width > 0).ToArray();
        var viewport = this.FindControl<Canvas>("CompactViewport")!;
        // Pause both lines together so the heading and its roster stay on the same assignment.
        if (headings.Any(x => x.IsPointerOver) || (!ViewModel.Expanded && viewport.IsPointerOver)) return;
        _tickerSeconds += Math.Clamp(elapsed, 0, 0.25);
        const double speed = 28;
        var distance = 0.0;
        foreach (var heading in headings)
        {
            var overflow = Math.Max(0, heading.Extent.Width - heading.Viewport.Width);
            heading.Offset = new Vector(Math.Clamp((_tickerSeconds - 2) * speed, 0, overflow), 0);
            distance = Math.Max(distance, overflow);
        }
        if (!ViewModel.Expanded)
        {
            var text = this.FindControl<TextBlock>("CompactTicker")!;
            var overflow = Math.Max(0, text.Bounds.Width - viewport.Bounds.Width);
            Canvas.SetLeft(text, -Math.Clamp((_tickerSeconds - 2) * speed, 0, overflow));
            distance = Math.Max(distance, overflow);
        }
        // Give the longest line time to reach its end before advancing to the next assignment.
        if (_tickerSeconds >= (distance > 0 ? 4 + distance / speed : 6))
        {
            _tickerSeconds = 0;
            foreach (var heading in headings) heading.Offset = default;
            if (!ViewModel.Expanded) _ = AnimateSwitchCardAsync();
        }
    }
    private bool _isSwitchingCard;
    private async Task AnimateSwitchCardAsync()
    {
        if (_isSwitchingCard) return;
        _isSwitchingCard = true;
        try
        {
            var container = this.FindControl<StackPanel>("CompactContentContainer");
            if (container != null && ViewModel.Cards.Count > 1)
            {
                var animOut = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(120),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Setters = { new Setter(OpacityProperty, 1.0), new Setter(TranslateTransform.YProperty, 0.0) } },
                        new KeyFrame { KeyTime = TimeSpan.FromMilliseconds(120), Setters = { new Setter(OpacityProperty, 0.0), new Setter(TranslateTransform.YProperty, -8.0) },
                                       KeySpline = KeySpline.Parse("0.4, 0, 0.2, 1", CultureInfo.InvariantCulture) }
                    }
                };
                await animOut.RunAsync(container);

                ViewModel.AdvanceCompactCard();

                var animIn = new Animation
                {
                    Duration = TimeSpan.FromMilliseconds(200),
                    FillMode = FillMode.Forward,
                    Children =
                    {
                        new KeyFrame { KeyTime = TimeSpan.FromMilliseconds(0), Setters = { new Setter(OpacityProperty, 0.0), new Setter(TranslateTransform.YProperty, 10.0) } },
                        new KeyFrame { KeyTime = TimeSpan.FromMilliseconds(200), Setters = { new Setter(OpacityProperty, 1.0), new Setter(TranslateTransform.YProperty, 0.0) },
                                       KeySpline = KeySpline.Parse("0.16, 1, 0.3, 1", CultureInfo.InvariantCulture) }
                    }
                };
                await animIn.RunAsync(container);
            }
            else
            {
                ViewModel.AdvanceCompactCard();
            }
        }
        catch
        {
            ViewModel.AdvanceCompactCard();
        }
        finally
        {
            _isSwitchingCard = false;
        }
    }
    private void Close_OnClick(object? sender, RoutedEventArgs e) => Close();
}
