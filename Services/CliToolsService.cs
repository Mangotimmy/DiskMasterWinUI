using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

/// <summary>
/// High-Performance Engineering Service for CMD CLI Tools Transfer.
/// Bridges cmd line utilities (ping, netstat, openfiles, nbtstat, attrib, where, fsutil)
/// into native graphical actions with full flag support.
/// </summary>
public class CliToolsService
{
    // ═════════════════════════════════════════════════════════════════════
    // 1. PING: Full Flags + Latency Streaming + Automatic Path MTU Discovery
    // ═════════════════════════════════════════════════════════════════════

    public async Task<int> RunPingAsync(
        PingOptionsModel options,
        Action<string> onOutputLine,
        CancellationToken cancellationToken = default)
    {
        var args = new List<string>();

        if (options.ResolveHostname) args.Add("-a");
        if (options.Continuous) args.Add("-t");
        else args.Add($"-n {Math.Clamp(options.Count, 1, 1000)}");

        args.Add($"-l {Math.Clamp(options.BufferSize, 32, 65500)}");
        if (options.DontFragment) args.Add("-f");
        args.Add($"-i {Math.Clamp(options.Ttl, 1, 255)}");
        args.Add($"-w {Math.Clamp(options.TimeoutMs, 100, 30000)}");

        if (options.ForceIPv4) args.Add("-4");
        else if (options.ForceIPv6) args.Add("-6");

        args.Add($"\"{options.TargetHost.Trim()}\"");

        var argumentString = string.Join(" ", args);
        onOutputLine($"[PING] Executing: ping.exe {argumentString}");

        var encoding = ProcessHelper.GetConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = argumentString,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) onOutputLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onOutputLine($"[STDERR] {e.Data}"); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var reg = cancellationToken.Register(() =>
            {
                try { proc.Kill(true); } catch { }
            });

            await proc.WaitForExitAsync(cancellationToken);
            return proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            onOutputLine("[INFO] Ping operation stopped by user.");
            return -1;
        }
        catch (Exception ex)
        {
            onOutputLine($"[EXCEPTION] Failed to run ping: {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Automatic Path MTU Discovery using recursive DF binary search.
    /// Discovers optimal MTU without fragmentation (Buffer Size + 28 bytes header).
    /// </summary>
    public async Task<(int OptimalMtu, string Summary)> DetectPathMtuAsync(
        string targetHost,
        Action<string> onProgress,
        CancellationToken ct = default)
    {
        onProgress($"[{DateTime.Now:HH:mm:ss}] 🔍 Starting Path MTU Discovery for {targetHost}...");

        int low = 1200;
        int high = 1500;
        int bestBuffer = -1;

        // Standard ICMP payloads to probe: 1472 (MTU 1500), 1464 (MTU 1492 PPPoE), 1372 (MTU 1400)
        int[] fastProbes = [1472, 1464, 1452, 1400];

        foreach (var probe in fastProbes)
        {
            if (ct.IsCancellationRequested) break;
            var ok = await TestPingDfAsync(targetHost, probe, ct);
            onProgress($" - Testing Buffer {probe} bytes (MTU {probe + 28}): {(ok ? "✅ PASS" : "❌ FRAGMENTED")}");
            if (ok)
            {
                bestBuffer = probe;
                break;
            }
        }

        if (bestBuffer == -1)
        {
            // Binary search down
            high = 1400;
            while (low <= high && !ct.IsCancellationRequested)
            {
                int mid = (low + high) / 2;
                var ok = await TestPingDfAsync(targetHost, mid, ct);
                onProgress($" - Binary Search Buffer {mid} bytes: {(ok ? "✅ PASS" : "❌ FRAGMENTED")}");
                if (ok)
                {
                    bestBuffer = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
        }

        if (bestBuffer > 0)
        {
            int optimalMtu = bestBuffer + 28;
            string summary = $"Discovered Optimal MTU: {optimalMtu} bytes (Payload {bestBuffer}B + 28B IP/ICMP Header)";
            onProgress($"[{DateTime.Now:HH:mm:ss}] 🎯 {summary}");
            return (optimalMtu, summary);
        }

        return (1500, "Unable to discover MTU (Host unreachable or ICMP blocked). Default standard: 1500 bytes.");
    }

    private async Task<bool> TestPingDfAsync(string host, int bufferSize, CancellationToken ct)
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("ping.exe", $"-n 1 -f -l {bufferSize} -w 1000 \"{host}\"", cancellationToken: ct);
        if (code != 0) return false;
        if (outStr.Contains("fragmented", StringComparison.OrdinalIgnoreCase) ||
            outStr.Contains("分散", StringComparison.OrdinalIgnoreCase) ||
            outStr.Contains("分段", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return outStr.Contains("TTL=", StringComparison.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. NETSTAT: Full Flags + Port Occupant Hunter + 1-Click Process Killer
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<NetstatConnectionItem>> GetActiveConnectionsAsync(int? filterPort = null, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var list = new List<NetstatConnectionItem>();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                var (outStr, _, code) = await ProcessHelper.RunProcessAsync("netstat.exe", "-ano", cancellationToken: timeoutCts.Token);
                if (string.IsNullOrWhiteSpace(outStr)) return list;

                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase) &&
                        !trimmed.StartsWith("UDP", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;

                    string proto = parts[0];
                    string local = parts[1];
                    string foreign = parts[2];
                    string state = "";
                    int pid = 0;

                    if (proto.Equals("TCP", StringComparison.OrdinalIgnoreCase))
                    {
                        if (parts.Length >= 5)
                        {
                            state = parts[3];
                            int.TryParse(parts[4], out pid);
                        }
                    }
                    else
                    {
                        state = "N/A";
                        if (parts.Length >= 4) int.TryParse(parts[3], out pid);
                    }

                    var (localIp, localPort) = ParseAddressAndPort(local);
                    var (foreignIp, foreignPort) = ParseAddressAndPort(foreign);

                    if (filterPort.HasValue && localPort != filterPort.Value && foreignPort != filterPort.Value)
                    {
                        continue;
                    }

                    string procName = "";
                    string procPath = "";
                    if (pid > 0)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(pid);
                            procName = proc.ProcessName;
                            try { procPath = proc.MainModule?.FileName ?? ""; } catch { }
                        }
                        catch
                        {
                            procName = "(Exited / System)";
                        }
                    }

                    list.Add(new NetstatConnectionItem
                    {
                        Protocol = proto,
                        LocalAddress = localIp,
                        LocalPort = localPort,
                        ForeignAddress = foreignIp,
                        ForeignPort = foreignPort,
                        State = state,
                        Pid = pid,
                        ProcessName = procName,
                        ExecutablePath = procPath
                    });
                }
            }
            catch { }
            return list;
        }, ct);
    }

    private static (string Ip, int Port) ParseAddressAndPort(string endpoint)
    {
        var lastColon = endpoint.LastIndexOf(':');
        if (lastColon <= 0) return (endpoint, 0);
        var ip = endpoint.Substring(0, lastColon).Trim('[', ']');
        int.TryParse(endpoint.Substring(lastColon + 1), out var port);
        return (ip, port);
    }

    public async Task<(bool Success, string Message)> KillProcessHoldingPortAsync(int port)
    {
        var conns = await GetActiveConnectionsAsync(port);
        var pids = conns.Where(c => c.LocalPort == port && c.Pid > 0).Select(c => c.Pid).Distinct().ToList();

        if (pids.Count == 0)
        {
            return (false, $"No active process found listening on port {port}.");
        }

        var results = new List<string>();
        foreach (var pid in pids)
        {
            var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("taskkill.exe", $"/F /PID {pid} /T");
            if (code == 0)
            {
                results.Add($"Terminated PID {pid} successfully.");
            }
            else
            {
                results.Add($"Failed to terminate PID {pid}: {errStr}");
            }
        }

        return (true, string.Join("; ", results));
    }

    public async Task<(bool Success, string Message)> KillProcessByPidAsync(int pid)
    {
        if (pid <= 0) return (false, "無效的 PID。");
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("taskkill.exe", $"/F /PID {pid} /T");
        if (code == 0)
        {
            return (true, $"已成功強制終結 PID {pid} 程序！");
        }
        return (false, $"終結 PID {pid} 失敗: {errStr}");
    }

    public async Task<(bool Success, double LatencyMs, string Message)> TestRemotePortAsync(string host, int port, int timeoutMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
        {
            return (false, 0, "請輸入有效的主機名稱/IP 與連接埠 (1-65535)。");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return (true, Math.Round(sw.Elapsed.TotalMilliseconds, 1), $"🟢 連線成功！目標 {host}:{port} 正常開放連線，回應延遲: {sw.Elapsed.TotalMilliseconds:F1} ms");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return (false, 0, $"🔴 連線逾時！目標 {host}:{port} 未在 {timeoutMs}ms 內回應 (連接埠可能未開放或被防火牆阻擋)。");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, 0, $"🔴 連線失敗！無法連接至 {host}:{port} ({ex.Message})");
        }
    }

    public async Task<(bool Success, string Message)> InstallTelnetClientAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /Enable-Feature /FeatureName:TelnetClient /NoRestart");
        if (code == 0)
        {
            return (true, "✅ Windows 內建 Telnet 用戶端功能已成功啟用安裝！");
        }
        return (false, $"❌ 安裝失敗 (代碼 {code}): {errStr}");
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. OPENFILES: Query SMB File Locks + 1-Click Forced Disconnect
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<OpenSharedFileItem>> GetOpenSharedFilesAsync(CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var list = new List<OpenSharedFileItem>();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(4));
                var (outStr, _, code) = await ProcessHelper.RunProcessAsync("openfiles.exe", "/query /fo csv /nh", cancellationToken: timeoutCts.Token);
                if (string.IsNullOrWhiteSpace(outStr)) return list;

                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries)
                        .Where(p => p != ",")
                        .ToArray();

                    if (parts.Length >= 4)
                    {
                        list.Add(new OpenSharedFileItem
                        {
                            Id = parts[0],
                            AccessedBy = parts[1],
                            Type = parts[2],
                            OpenMode = parts.Length > 4 ? parts[3] : "",
                            OpenPath = parts[^1]
                        });
                    }
                }
            }
            catch { }
            return list;
        }, ct);
    }

    public async Task<(bool Success, string Message)> DisconnectOpenFileAsync(string idOrPath)
    {
        string args = int.TryParse(idOrPath, out _) ? $"/disconnect /id {idOrPath}" : $"/disconnect /op \"{idOrPath}\"";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("openfiles.exe", args);
        return (code == 0, code == 0 ? (string.IsNullOrWhiteSpace(outStr) ? "Disconnected lock successfully." : outStr) : errStr);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 4. NBTSTAT: Cache Flush & Refresh (-R, -RR) + Node Inspector
    // ═════════════════════════════════════════════════════════════════════

    public async Task<(bool Success, string Message)> FlushAndRefreshNetBiosCacheAsync()
    {
        var (outR, errR, codeR) = await ProcessHelper.RunProcessAsync("nbtstat.exe", "-R");
        var (outRr, errRr, codeRr) = await ProcessHelper.RunProcessAsync("nbtstat.exe", "-RR");
        return (codeR == 0, $"Purged remote NetBIOS table: {outR.Trim()}\r\nRefreshed NetBIOS names: {outRr.Trim()}");
    }

    public async Task<string> InspectNetBiosNodeAsync(string ipOrHost)
    {
        var flag = IPAddress.TryParse(ipOrHost, out _) ? "-A" : "-a";
        var (outStr, errStr, _) = await ProcessHelper.RunProcessAsync("nbtstat.exe", $"{flag} {ipOrHost}");
        return string.IsNullOrWhiteSpace(outStr) ? errStr : outStr;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 5. ATTRIB: Virus Unhide Rescue (+R -R +S -S +H -H) & Security Lock
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Rescues USB or HDD drives where malware hid files using +s +h +r.
    /// Executes: attrib -r -a -s -h /S /D <DriveOrPath>\*.*
    /// </summary>
    public async Task<(bool Success, string Message)> RescueVirusHiddenFilesAsync(string targetPath)
    {
        var cleanPath = targetPath.TrimEnd('\\');
        var wildcard = cleanPath + @"\*.*";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("attrib.exe", $"-r -a -s -h /S /D \"{wildcard}\"");
        return (code == 0, code == 0 ? $"Successfully unhidden all virus-masked files and directories in {targetPath}!" : errStr);
    }

    public async Task<(bool Success, string Message)> ApplyAttributesAsync(string targetPath, string flags, bool recursive)
    {
        var recFlag = recursive ? "/S /D" : "";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("attrib.exe", $"{flags} \"{targetPath}\" {recFlag}");
        return (code == 0, code == 0 ? "Attributes updated successfully." : errStr);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 6. WHERE: Path Resolution & Executable Precedence Conflict Hunter
    // ═════════════════════════════════════════════════════════════════════

    public async Task<List<WhereResultItem>> LocateExecutableWithConflictsAsync(string commandPattern, string? rootDir = null, CancellationToken ct = default)
    {
        return await Task.Run(async () =>
        {
            var list = new List<WhereResultItem>();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
                var rArg = string.IsNullOrWhiteSpace(rootDir) ? "" : $"/R \"{rootDir}\"";
                var (outStr, _, code) = await ProcessHelper.RunProcessAsync("where.exe", $"{rArg} /T /F {commandPattern}", cancellationToken: timeoutCts.Token);
                if (string.IsNullOrWhiteSpace(outStr)) return list;

                var lines = outStr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                int rank = 1;

                foreach (var line in lines)
                {
                    // Format: <size> <date> <time> "<path>"
                    var match = Regex.Match(line.Trim(), @"^(\d+)\s+([^\s]+)\s+([^\s]+)\s+""([^""]+)""$");
                    if (match.Success)
                    {
                        long.TryParse(match.Groups[1].Value, out var size);
                        var dateStr = $"{match.Groups[2].Value} {match.Groups[3].Value}";
                        DateTime.TryParse(dateStr, out var dt);
                        var path = match.Groups[4].Value;

                        list.Add(new WhereResultItem
                        {
                            CommandName = Path.GetFileName(path),
                            FullPath = path,
                            SizeBytes = size,
                            DisplaySize = TopLargeFileItem.FormatBytes(size),
                            Timestamp = dt,
                            PrecedenceRank = rank++,
                            IsConflicting = list.Count > 0 // Secondary matches are shadowed
                        });
                    }
                    else if (line.Contains('"'))
                    {
                        var quotedPath = line.Substring(line.IndexOf('"')).Trim('"');
                        list.Add(new WhereResultItem
                        {
                            CommandName = Path.GetFileName(quotedPath),
                            FullPath = quotedPath,
                            PrecedenceRank = rank++,
                            IsConflicting = list.Count > 0
                        });
                    }
                }
            }
            catch { }
            return list;
        });
    }

    // ═════════════════════════════════════════════════════════════════════
    // 7. FSUTIL: SSD TRIM Status & 1-Click Optimizer + Volume Dirty + Dummy File
    // ═════════════════════════════════════════════════════════════════════

    public async Task<(bool IsTrimEnabled, string Message)> GetTrimStatusAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("fsutil.exe", "behavior query DisableDeleteNotify");
        if (code != 0) return (false, errStr);

        // 0 = Enabled (Trim supported), 1 = Disabled
        bool enabled = outStr.Contains("DisableDeleteNotify = 0");
        return (enabled, outStr.Trim());
    }

    public async Task<(bool Success, string Message)> SetTrimStatusAsync(bool enable)
    {
        int flag = enable ? 0 : 1;
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"behavior set DisableDeleteNotify {flag}");
        return (code == 0, code == 0 ? $"SSD TRIM has been successfully {(enable ? "ENABLED" : "DISABLED")}." : errStr);
    }

    public async Task<string> QueryDirtyFlagAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        var (outStr, errStr, _) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"dirty query {clean}");
        return string.IsNullOrWhiteSpace(outStr) ? errStr : outStr.Trim();
    }

    public async Task<(bool Success, string Message)> SetDirtyFlagAsync(string driveLetter)
    {
        var clean = driveLetter.TrimEnd(':', '\\') + ":";
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"dirty set {clean}");
        return (code == 0, code == 0 ? $"Volume {clean} marked as dirty. Chkdsk will verify it on next boot." : errStr);
    }

    public async Task<(bool Success, string Message)> CreateDummyFileAsync(string targetFilePath, long sizeBytes)
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("fsutil.exe", $"file createnew \"{targetFilePath}\" {sizeBytes}");
        return (code == 0, code == 0 ? $"Created {TopLargeFileItem.FormatBytes(sizeBytes)} dummy file at {targetFilePath}" : errStr);
    }
}
