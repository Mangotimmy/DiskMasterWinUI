namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Detects chkdsk's interactive schedule/dismount prompts and the locked-volume messages.
/// These prompts block the process because a WinUI app has no console stdin; the detector
/// lets the streaming runner answer N (do not schedule a boot-time scan) and lets the UI
/// report a real failure instead of a false running countdown.
/// </summary>
public static class ChkdskPromptDetector
{
    public const string DeclineResponse = "N";

    public static bool IsVolumeBusy(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ContainsAny(text,
            "無法鎖定目前的磁碟機",
            "无法锁定当前的磁盘驱动器",
            "Cannot lock current drive",
            "無法執行 Chkdsk",
            "无法执行 Chkdsk",
            "Chkdsk cannot run",
            "磁碟區正由其他處理程序使用中",
            "卷正被另一进程使用",
            "volume is in use by another process",
            "ボリュームは別のプロセスで使用");
    }

    public static bool IsSchedulePrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return ContainsAny(text,
            "是否要將此磁碟區排程",
            "是否要将此磁盘区排程",
            "是否要将此卷安排",
            "排程在下次系統重新啟動時",
            "安排在下次系统重新启动时",
            "Would you like to schedule this volume",
            "schedule this volume to be checked",
            "次回のシステム起動時に",
            "次回のシステム起動時にチェック");
    }

    public static bool TryGetResponse(string text, out string response)
    {
        response = DeclineResponse;
        return IsSchedulePrompt(text);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (text.Contains(needle, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
