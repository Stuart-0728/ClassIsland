using System;
using Avalonia;
using Avalonia.Controls;
using ClassIsland.Core.Models.Weather;

namespace ClassIsland.Controls;

public partial class WeatherAlertIconControl : UserControl
{
    public static readonly StyledProperty<WeatherAlert?> AlertProperty =
        AvaloniaProperty.Register<WeatherAlertIconControl, WeatherAlert?>(nameof(Alert));

    public WeatherAlert? Alert
    {
        get => GetValue(AlertProperty);
        set => SetValue(AlertProperty, value);
    }

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<WeatherAlertIconControl, double>(nameof(IconSize), 22.0);

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly DirectProperty<WeatherAlertIconControl, WeatherAlert?> DisplayAlertProperty =
        AvaloniaProperty.RegisterDirect<WeatherAlertIconControl, WeatherAlert?>(
            nameof(DisplayAlert),
            o => o.DisplayAlert);

    private WeatherAlert? _displayAlert;

    public WeatherAlert? DisplayAlert
    {
        get => _displayAlert;
        private set => SetAndRaise(DisplayAlertProperty, ref _displayAlert, value);
    }

    public double VectorIconSize => Math.Max(11.0, IconSize * 0.6);

    public WeatherAlertIconControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AlertProperty || change.Property == DataContextProperty)
        {
            DisplayAlert = Alert ?? DataContext as WeatherAlert;
        }
    }
}
