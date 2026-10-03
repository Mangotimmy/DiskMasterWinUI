using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

public sealed partial class BitLockerUnlockDialog : ContentDialog
{
    private readonly BitLockerService _bitLockerService = new();
    public string DriveLetter { get; set; } = "D:";
    public bool UnlockSucceeded { get; private set; }

    public BitLockerUnlockDialog(string driveLetter)
    {
        InitializeComponent();
        DriveLetter = driveLetter.TrimEnd('\\', ':') + ":";
        DriveTitleText.Text = $"磁碟機: {DriveLetter}";
    }

    private void ModeRadioButtons_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecoveryKeyPanel == null || PasswordPanel == null) return;

        if (ModeRadioButtons.SelectedIndex == 0)
        {
            RecoveryKeyPanel.Visibility = Visibility.Visible;
            PasswordPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            RecoveryKeyPanel.Visibility = Visibility.Collapsed;
            PasswordPanel.Visibility = Visibility.Visible;
        }
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        // Defer closing until unlock attempt finishes
        var deferral = args.GetDeferral();
        try
        {
            bool success;
            string message;

            if (ModeRadioButtons.SelectedIndex == 0)
            {
                var key = RecoveryKeyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    ErrorInfoBar.Message = "請輸入 48 位元修復金鑰。";
                    ErrorInfoBar.IsOpen = true;
                    args.Cancel = true;
                    return;
                }

                (success, message) = await _bitLockerService.UnlockWithRecoveryKeyAsync(DriveLetter, key);
            }
            else
            {
                var pass = DrivePasswordBox.Password;
                if (string.IsNullOrWhiteSpace(pass))
                {
                    ErrorInfoBar.Message = "請輸入磁碟解鎖密碼。";
                    ErrorInfoBar.IsOpen = true;
                    args.Cancel = true;
                    return;
                }

                (success, message) = await _bitLockerService.UnlockWithPasswordAsync(DriveLetter, pass);
            }

            if (success)
            {
                UnlockSucceeded = true;
                args.Cancel = false;
            }
            else
            {
                ErrorInfoBar.Message = message;
                ErrorInfoBar.IsOpen = true;
                args.Cancel = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
