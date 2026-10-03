namespace DiskMasterWinUI;

public static class WindowHelper
{
    public static nint CurrentHwnd { get; set; } = nint.Zero;

    public static Microsoft.UI.Xaml.XamlRoot? RootXamlRoot { get; set; }

    public static nint GetWindowHandle() => CurrentHwnd;

    public static Microsoft.UI.Xaml.XamlRoot? GetXamlRoot() => RootXamlRoot;
}
