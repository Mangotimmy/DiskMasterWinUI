using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class GroupPolicyPage : Page
{
    public GroupPolicyViewModel ViewModel { get; } = new();

    public GroupPolicyPage()
    {
        InitializeComponent();
        DataContext = ViewModel;
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    private void DisableAutoReboot_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableAutoRebootCommand.Execute(null);
    }

    private void DisableTelemetry_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableTelemetryCommand.Execute(null);
    }

    private void DisableSearchBox_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableSearchBoxSuggestionsCommand.Execute(null);
    }

    private void DisableConsumerFeatures_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableConsumerFeaturesCommand.Execute(null);
    }

    private void DisableDeliveryOptimization_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableDeliveryOptimizationCommand.Execute(null);
    }

    private void DisableUacSecureDesktop_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableUacSecureDesktopCommand.Execute(null);
    }

    private void DisableDefenderSample_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleDisableDefenderSampleSubmissionCommand.Execute(null);
    }

    private void ForceHags_Toggled(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleForceHagsCommand.Execute(null);
    }
}
