using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class GroupPolicyPage : Page
{
    public GroupPolicyViewModel ViewModel { get; } = new();

    // The ToggleSwitch controls are bound OneWay to the ViewModel, so every ViewModel
    // write pushes IsOn, and every IsOn change (user or binding) raises Toggled. If the
    // Toggled handler ran on a binding push, its toggle command would flip the property
    // again, which pushes IsOn again, which raises Toggled again — an unbounded
    // recursion that exhausts the UI thread stack and surfaces as
    // "無法建立新的堆疊防護分頁" (cannot create a new stack guard page).
    // This flag is true while the ViewModel drives the switches and false once the page
    // is built, so the handlers only ever act on a real user gesture.
    private bool _suppressToggled = true;
    private bool _hasLoadedOnce;

    public GroupPolicyPage()
    {
        // InitializeComponent applies the x:Bind initial values, which raises Toggled
        // for every policy that is already enabled — _suppressToggled is still true here.
        InitializeComponent();
        DataContext = ViewModel;
        NavigationCacheMode = NavigationCacheMode.Required;
        _suppressToggled = false;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoadedOnce) return;
        _hasLoadedOnce = true;

        // Re-read the live policy state (another page may have changed the same policies)
        // without letting those binding pushes re-enter the toggle handlers.
        _suppressToggled = true;
        try
        {
            ViewModel.RefreshStatus();
        }
        finally
        {
            _suppressToggled = false;
        }
    }

    private void DisableAutoReboot_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableAutoReboot) return;
        ViewModel.ToggleDisableAutoRebootCommand.Execute(null);
    }

    private void DisableTelemetry_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableTelemetry) return;
        ViewModel.ToggleDisableTelemetryCommand.Execute(null);
    }

    private void DisableSearchBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableSearchBoxSuggestions) return;
        ViewModel.ToggleDisableSearchBoxSuggestionsCommand.Execute(null);
    }

    private void DisableConsumerFeatures_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableConsumerFeatures) return;
        ViewModel.ToggleDisableConsumerFeaturesCommand.Execute(null);
    }

    private void DisableDeliveryOptimization_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableDeliveryOptimization) return;
        ViewModel.ToggleDisableDeliveryOptimizationCommand.Execute(null);
    }

    private void DisableUacSecureDesktop_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableUacSecureDesktop) return;
        ViewModel.ToggleDisableUacSecureDesktopCommand.Execute(null);
    }

    private void DisableDefenderSample_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsDisableDefenderSampleSubmission) return;
        ViewModel.ToggleDisableDefenderSampleSubmissionCommand.Execute(null);
    }

    private void ForceHags_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch ts && ts.IsOn == ViewModel.IsForceHags) return;
        ViewModel.ToggleForceHagsCommand.Execute(null);
    }
}
