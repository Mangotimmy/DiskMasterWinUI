using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Controls;

public sealed partial class ShareTroubleshootingDialog : ContentDialog
{
    public NetworkToolsViewModel ViewModel { get; }

    public ShareTroubleshootingDialog(NetworkToolsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void FixPolicy_Click(object sender, RoutedEventArgs e) => ViewModel.FixMsAccountPolicy();

    private async void PrivateNetwork_Click(object sender, RoutedEventArgs e) => await ViewModel.SetPrivateNetworkAsync();

    private async void Firewall_Click(object sender, RoutedEventArgs e) => await ViewModel.EnableSharingFirewallAsync();

    private async void CreateUser_Click(object sender, RoutedEventArgs e) => await ViewModel.CreateDedicatedUserAsync();
}
