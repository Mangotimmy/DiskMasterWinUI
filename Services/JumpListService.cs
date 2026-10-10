using Windows.UI.StartScreen;

namespace DiskMasterWinUI.Services;

/// <summary>
/// Registers Windows Taskbar JumpList Quick Action tasks for fast navigation.
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

            var taskEasy = JumpListItem.CreateWithArguments("--tab EasyMode", "🚀 簡易體檢模式");
            taskEasy.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskEasy);

            var taskPerf = JumpListItem.CreateWithArguments("--tab SystemPerformance", "📊 系統即時效能");
            taskPerf.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskPerf);

            var taskNet = JumpListItem.CreateWithArguments("--tab NetworkTools", "🌐 網路工具與診斷");
            taskNet.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskNet);

            var taskDisk = JumpListItem.CreateWithArguments("--tab DiskTools", "💾 磁碟工具與救援");
            taskDisk.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskDisk);

            var taskHealth = JumpListItem.CreateWithArguments("--tab SystemHealth", "🛠️ 系統修復 (DISM/SFC)");
            taskHealth.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskHealth);

            var taskSettings = JumpListItem.CreateWithArguments("--tab Settings", "⚙️ 偏好設定");
            taskSettings.GroupName = "快捷功能 (Quick Actions)";
            jumpList.Items.Add(taskSettings);

            await jumpList.SaveAsync();
        }
        catch { }
    }
}
