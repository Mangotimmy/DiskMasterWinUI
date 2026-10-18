using System.Management;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Small WMI helper that always applies connection/query timeouts. System.Management's
/// defaults are effectively infinite, which is how a busy storage provider can park the
/// DiskTools refresh for minutes with no way to recover.
/// </summary>
public static class WmiQueryHelper
{
    public static List<T> Query<T>(
        string scopePath,
        string query,
        TimeSpan timeout,
        Func<ManagementObject, T> map)
    {
        var result = new List<T>();
        var connection = new ConnectionOptions { Timeout = timeout };
        var scope = new ManagementScope(scopePath, connection);
        scope.Connect();

        var options = new System.Management.EnumerationOptions
        {
            Timeout = timeout,
            ReturnImmediately = false,
            Rewindable = false
        };

        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query), options);
        using var collection = searcher.Get();
        foreach (ManagementObject obj in collection)
        {
            using (obj)
            {
                result.Add(map(obj));
            }
        }
        return result;
    }
}
