using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Controls;

public sealed partial class ShareEditorDialog : ContentDialog
{
    public ShareEditorViewModel ViewModel { get; }
    public bool Saved { get; private set; }
    public string ResultMessage { get; private set; } = "";

    public ShareEditorDialog(NetShareItem? existing = null)
    {
        ViewModel = new ShareEditorViewModel(existing);
        InitializeComponent();
        Title = existing == null ? "New share" : "Share settings";
        PrimaryButtonText = existing == null ? "Create share" : "Save changes";
        DialogIntro.Text = existing == null
            ? "Choose a folder, name the share, then add the people who should have access."
            : "Update permissions and advanced options. The share name and folder cannot be changed here.";
        EditorAccountPicker.SelectedAccountChanged += (_, account) => { };
    }

    private async void Dialog_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private async void PrimaryButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        var (ok, message) = await ViewModel.SaveAsync();
        if (ok)
        {
            Saved = true;
            ResultMessage = message;
            Hide();
        }
        else
        {
            ViewModel.ValidationMessage = message;
        }
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        string? folder = await FilePickerHelper.PickFolderAsync("Choose a folder to share");
        if (string.IsNullOrWhiteSpace(folder)) return;
        ViewModel.FolderPath = folder;
        if (string.IsNullOrWhiteSpace(ViewModel.ShareName))
        {
            string leaf = Path.GetFileName(folder.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(leaf)) ViewModel.ShareName = leaf.Replace(" ", "_");
        }
    }

    private void AddPerson_Click(object sender, RoutedEventArgs e)
    {
        var account = EditorAccountPicker.SelectedAccount;
        if (account == null)
        {
            ViewModel.ValidationMessage = "Select an account first.";
            return;
        }
        string level = AccessLevelCombo.SelectedItem as string ?? "Read";
        ViewModel.AddGrant(account.AccountName, level);
    }

    private void RemoveGrant_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ShareAclGrantItem grant)
        {
            ViewModel.Grants.Remove(grant);
        }
    }
}
