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

        bool isZh = LocalizationService.Instance.IsChinese;

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
                Text = (isZh ? "受影響對象: " : "Target: ") + targetItemName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            panel.Children.Add(targetCard);
        }

        var warningText = new TextBlock
        {
            Text = isZh ? "⚠️ 此操作將永久修改或抹除資料，請確認後再繼續！" : "⚠️ This operation modifies or wipes data permanently. Confirm to proceed!",
            Foreground = Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold
        };
        panel.Children.Add(warningText);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "🔴 " + title,
            Content = panel,
            PrimaryButtonText = isZh ? "確認執行" : "Proceed",
            CloseButtonText = isZh ? "取消" : "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
