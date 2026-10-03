using Microsoft.UI.Dispatching;

namespace DiskMasterWinUI.Helpers;

public static class DispatcherHelper
{
    public static DispatcherQueue? UIDispatcher { get; set; }

    public static void RunOnUIThread(Action action)
    {
        if (UIDispatcher != null && !UIDispatcher.HasThreadAccess)
        {
            UIDispatcher.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch { }
            });
        }
        else
        {
            action();
        }
    }
}
