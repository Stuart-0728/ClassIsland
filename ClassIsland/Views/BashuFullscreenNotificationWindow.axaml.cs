using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace ClassIsland.Views;

public partial class BashuFullscreenNotificationWindow : Window
{
    public event EventHandler? Confirmed;

    public BashuFullscreenNotificationWindow()
    {
        InitializeComponent();
    }

    public BashuFullscreenNotificationWindow(string author, string content, bool isEmergency = false) : this()
    {
        if (TextBlockTitle != null)
        {
            TextBlockTitle.Text = isEmergency ? "【紧急广播通知】" : "班级重要通知";
        }
        if (TextBlockAuthor != null)
        {
            TextBlockAuthor.Text = string.IsNullOrWhiteSpace(author) ? "来自：任课教师" : $"来自：{author}";
        }
        if (TextBlockContent != null)
        {
            TextBlockContent.Text = content;
        }

        if (isEmergency)
        {
            if (CardBorder != null)
            {
                CardBorder.BorderBrush = new SolidColorBrush(Color.Parse("#EF4444"));
            }
            if (IconBorder != null)
            {
                IconBorder.Background = new SolidColorBrush(Color.Parse("#33EF4444"));
                IconBorder.BorderBrush = new SolidColorBrush(Color.Parse("#88EF4444"));
            }
            if (IconPath != null)
            {
                IconPath.Foreground = new SolidColorBrush(Color.Parse("#F87171"));
            }
            if (ButtonConfirm != null)
            {
                ButtonConfirm.Background = new SolidColorBrush(Color.Parse("#DC2626"));
            }
        }
    }

    private void ButtonConfirm_OnClick(object? sender, RoutedEventArgs e)
    {
        OnConfirmAndClose();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space or Key.Escape)
        {
            e.Handled = true;
            OnConfirmAndClose();
        }
    }

    private void OnConfirmAndClose()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
