using Windows.UI.StartScreen;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Registers Windows Taskbar JumpList Quick Action tasks for fast right-click navigation.
/// </summary>
public static class JumpListService
{
    public static async Task InitializeAsync()
    {
        try
        {
            if (!JumpList.IsSupported()) return;

            var jumpList = await JumpList.LoadCurrentAsync();
            jumpList.SystemGroupKind = JumpListSystemGroupKind.None;
            jumpList.Items.Clear();

            var items = new (string Args, string Title, string Group)[]
            {
                ("--tab EasyMode", "🚀 簡易體檢模式 (Easy Mode)", "核心工具 (Core Tools)"),
                ("--tab Recovery", "🛟 檔案復原救援 (File Recovery)", "核心工具 (Core Tools)"),
                ("--tab WimDeploy", "💿 系統映像部署 (WIM / ESD / ISO)", "核心工具 (Core Tools)"),
                ("--tab BootManager", "🔧 開機引導管理 (BCD / UEFI)", "核心工具 (Core Tools)"),
                ("--tab DiskTools", "💾 磁碟工具與環境變數 (Disk & Env)", "進階維護 (Advanced)"),
                ("--tab NtfsPermissions", "🛡️ NTFS 權限與取得所有權", "進階維護 (Advanced)"),
                ("--tab SystemOptimizer", "⚡ 系統極致加速與電競最佳化", "進階維護 (Advanced)"),
                ("--tab NetworkTools", "🌐 網路工具與 TCP 全域微調", "進階維護 (Advanced)"),
                ("--tab SystemPerformance", "📊 即時效能分析 (Performance)", "進階維護 (Advanced)"),
                ("--tab SystemHealth", "🛠️ 系統修復 (DISM / SFC)", "進階維護 (Advanced)"),
                ("--tab Settings", "⚙️ 偏好設定與右鍵選單整合", "設定 (Settings)")
            };

            foreach (var (args, title, group) in items)
            {
                var item = JumpListItem.CreateWithArguments(args, title);
                item.GroupName = group;
                jumpList.Items.Add(item);
            }

            await jumpList.SaveAsync();
        }
        catch { }
    }
}
