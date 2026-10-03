using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class NetworkToolsPage : Page
{
    public NetworkToolsViewModel ViewModel { get; } = new();

    public NetworkToolsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        RefreshNetBtnText.Text = lang switch
        {
            "zh-CN" => "刷新网络状态",
            "en-US" => "Refresh Network Status",
            "ja-JP" => "ネットワーク状態を更新",
            _ => "重新整理網路狀態"
        };

        TabUpnp.Header = lang switch
        {
            "zh-CN" => "🔀 UPnP 端口转发",
            "en-US" => "🔀 UPnP Port Forwarding",
            "ja-JP" => "🔀 UPnP ポート転送",
            _ => "🔀 UPnP 自動通訊埠轉發"
        };

        TabDns.Header = lang switch
        {
            "zh-CN" => "🌐 推荐 DNS 设置",
            "en-US" => "🌐 Recommended DNS",
            "ja-JP" => "🌐 推奨 DNS 設定",
            _ => "🌐 推薦 DNS 設定"
        };

        TabDiagnostic.Header = lang switch
        {
            "zh-CN" => "🩺 智能卡顿诊断",
            "en-US" => "🩺 Smart Diagnostics",
            "ja-JP" => "🩺 スマート診断",
            _ => "🩺 智慧連線卡頓診斷"
        };

        TabHosts.Header = lang switch
        {
            "zh-CN" => "📝 Hosts 文件编辑器",
            "en-US" => "📝 Hosts File Editor",
            "ja-JP" => "📝 Hosts エディタ",
            _ => "📝 Hosts 檔案編輯器"
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyLanguage();
        await ViewModel.LoadInitialDataCommand.ExecuteAsync(null);
    }

    private void PresetMinecraft_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 25565, InternalPort = 25565, Protocol = "TCP", Description = "Minecraft Java Server"
        });
    }

    private void PresetSteam_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 27015, InternalPort = 27015, Protocol = "UDP", Description = "Steam CS2 / TF2 Server"
        });
    }

    private void PresetTerraria_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 7777, InternalPort = 7777, Protocol = "TCP", Description = "Terraria Multiplayer"
        });
    }

    private void PresetPalworld_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 8211, InternalPort = 8211, Protocol = "UDP", Description = "Palworld Dedicated Server"
        });
    }

    private void PresetRdp_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 3389, InternalPort = 3389, Protocol = "TCP", Description = "Windows Remote Desktop (RDP)"
        });
    }

    private void PresetPlex_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplyGamePresetCommand.Execute(new UpnpPortMappingItem
        {
            ExternalPort = 32400, InternalPort = 32400, Protocol = "TCP", Description = "Plex Media Server"
        });
    }

    private async void DeletePortMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is UpnpPortMappingItem item)
        {
            await ViewModel.DeletePortMappingCommand.ExecuteAsync(item);
        }
    }
}
