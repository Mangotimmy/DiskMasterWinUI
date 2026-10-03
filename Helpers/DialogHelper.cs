using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

public static class DialogHelper
{
    public static async Task<bool> ConfirmDestructiveOperationAsync(
        XamlRoot? xamlRoot,
        string title,
        string message,
        string targetItemName)
    {
        if (!SettingsService.Instance.Current.EnableSafetyConfirmations)
        {
            return true;
        }

        if (xamlRoot == null) return true;

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrEmpty(targetItemName))
        {
            var targetCard = new Border
            {
                Background = Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"] as Microsoft.UI.Xaml.Media.Brush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                BorderThickness = new Thickness(1),
                BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Microsoft.UI.Xaml.Media.Brush
            };
            targetCard.Child = new TextBlock
            {
                Text = LocalizationService.Instance["TargetItem"] + " " + targetItemName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            panel.Children.Add(targetCard);
        }

        var warningText = new TextBlock
        {
            Text = LocalizationService.Instance["WarningDestructive"],
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        panel.Children.Add(warningText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "🔴 " + title,
            Content = panel,
            PrimaryButtonText = LocalizationService.T("確認執行", "确认执行", "Proceed", "続行"),
            CloseButtonText = LocalizationService.Instance["Cancel"],
            DefaultButton = ContentDialogButton.Close
        };

        try
        {
            if (SettingsService.Instance.Current.EnableTaskbarFlash)
            {
                TaskbarFlashService.Flash(WindowHelper.CurrentHwnd, 0);
            }
            AudioFeedbackService.PlayWarning();

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        finally
        {
            TaskbarFlashService.StopFlash(WindowHelper.CurrentHwnd);
        }
    }
}
