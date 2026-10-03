using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Controls;

public sealed partial class FloatingHudControl : UserControl
{
    public StatusBarViewModel ViewModel => StatusBarViewModel.Instance;

    public event RoutedEventHandler? RestoreRequested;

    public FloatingHudControl()
    {
        InitializeComponent();
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        RestoreRequested?.Invoke(this, e);
    }
}
