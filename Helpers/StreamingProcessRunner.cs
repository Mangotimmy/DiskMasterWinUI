using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

public sealed record StreamingProcessResult(int ExitCode, TimeSpan Duration, bool Cancelled);

/// <summary>
/// Streams a console process while preserving both CR and LF line boundaries. The old
/// OutputDataReceived approach only delivered completed lines, so chkdsk/DISM progress that
/// uses carriage returns never reached the page or the ETA tracker. Every logical line is
/// forwarded to the caller and recorded as an OUTPUT row in DebugLogService.
/// </summary>
public static class StreamingProcessRunner
{
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    public static async Task<StreamingProcessResult> RunAsync(
        string category,
        string fileName,
        string arguments,
        Action<string> onLine,
        CancellationToken cancellationToken = default,
        Func<string, string?>? inputResponder = null,
        TimeSpan? idleTimeout = null)
    {
        var encoding = ProcessHelper.GetConsoleEncoding();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stopwatch = Stopwatch.StartNew();
        DebugLogService.Instance.LogRequest(category, $"啟動呼叫: {fileName} {arguments}", arguments);

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            // A WinUI app has no console stdin. Without this close, a command that asks
            // a Y/N question can block forever on an inherited or invalid handle.
            if (inputResponder == null)
            {
                try { process.StandardInput.Close(); } catch { }
            }

            object stdinLock = new();
            long promptAnsweredTicks = 0;
            void RespondToText(string text)
            {
                if (inputResponder == null || string.IsNullOrWhiteSpace(text)) return;

                string? response;
                try { response = inputResponder(text); }
                catch (Exception ex)
                {
                    DebugLogService.Instance.Warning($"處理命令提示失敗: {ex.Message}", category);
                    return;
                }

                if (response == null) return;
                string reply = response.TrimEnd('\r', '\n');

                try
                {
                    lock (stdinLock)
                    {
                        process.StandardInput.WriteLine(reply);
                        process.StandardInput.Flush();
                    }
                    Volatile.Write(ref promptAnsweredTicks, Environment.TickCount64);
                    TryInvoke(onLine, $"[INPUT] {reply}");
                    DebugLogService.Instance.LogCommandOutput(category, $"[INPUT] {reply}");
                }
                catch (Exception ex)
                {
                    DebugLogService.Instance.Warning($"寫入命令輸入失敗: {ex.Message}", category);
                }
            }

            long lastOutputTicks = Environment.TickCount64;
            int idleTimedOut = 0;
            Timer? idleTimer = null;
            if (idleTimeout is { TotalMilliseconds: > 0 } timeout)
            {
                idleTimer = new Timer(_ =>
                {
                    try
                    {
                        if (process.HasExited) return;

                        long now = Environment.TickCount64;
                        long answeredAt = Volatile.Read(ref promptAnsweredTicks);
                        bool promptStuck = answeredAt != 0 && now - answeredAt > 15_000;
                        bool idle = now - Volatile.Read(ref lastOutputTicks) >= timeout.TotalMilliseconds;
                        if (!promptStuck && !idle) return;
                        if (Interlocked.Exchange(ref idleTimedOut, 1) != 0) return;

                        TryInvoke(onLine, promptStuck
                            ? $"[TIMEOUT] Prompt was answered but {fileName} did not exit; terminating it."
                            : $"[TIMEOUT] No output for {timeout.TotalSeconds:F0}s; terminating {fileName}.");
                        DebugLogService.Instance.Warning(
                            promptStuck
                                ? $"命令已回答提示但未結束，強制終止: {fileName} {arguments}"
                                : $"命令閒置逾時 ({timeout.TotalSeconds:F0}s): {fileName} {arguments}",
                            category);
                        try { process.Kill(true); } catch { }
                    }
                    catch { }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }

            void MarkOutput(string text)
            {
                Volatile.Write(ref lastOutputTicks, Environment.TickCount64);
                RespondToText(text);
            }

            var stdoutTask = PumpAsync(process.StandardOutput, false, category, onLine, MarkOutput, cancellationToken);
            var stderrTask = PumpAsync(process.StandardError, true, category, onLine, MarkOutput, cancellationToken);

            using var registration = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch { }
            });

            await process.WaitForExitAsync(cancellationToken);
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                DebugLogService.Instance.Warning($"輸出串流在程序結束後仍未關閉: {fileName}", category);
            }

            process.WaitForExit(200);
            idleTimer?.Dispose();
            stopwatch.Stop();

            bool timedOut = Volatile.Read(ref idleTimedOut) != 0;
            int exitCode = timedOut ? -3 : process.ExitCode;
            DebugLogService.Instance.LogRequest(
                category,
                timedOut
                    ? $"逾時終止: {fileName}"
                    : $"完成呼叫: {fileName} (ExitCode: {exitCode})",
                arguments,
                stopwatch.ElapsedMilliseconds,
                !timedOut && exitCode == 0);
            return new StreamingProcessResult(exitCode, stopwatch.Elapsed, false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            DebugLogService.Instance.LogRequest(category, $"取消呼叫: {fileName}", arguments, stopwatch.ElapsedMilliseconds, false);
            TryInvoke(onLine, "[INFO] Operation cancelled by user.");
            return new StreamingProcessResult(-1, stopwatch.Elapsed, true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            DebugLogService.Instance.LogRequest(category, $"異常失敗: {fileName} ({ex.Message})", arguments, stopwatch.ElapsedMilliseconds, false);
            TryInvoke(onLine, $"[EXCEPTION] Failed to execute {fileName}: {ex.Message}");
            return new StreamingProcessResult(-1, stopwatch.Elapsed, false);
        }
    }

    private static async Task PumpAsync(
        StreamReader reader,
        bool isError,
        string category,
        Action<string> onLine,
        Action<string> onTextSeen,
        CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var current = new StringBuilder();
        bool previousWasCarriageReturn = false;
        long lastProgressLogTicks = 0;

        void Emit(string raw)
        {
            string line = Normalize(raw);
            if (string.IsNullOrWhiteSpace(line)) return;

            if (isError) line = "[STDERR] " + line;

            TryInvoke(onTextSeen, line);
            TryInvoke(onLine, line);

            // Progress animations can fire dozens of times per second. The page/ETA still sees
            // every fragment, but the live console and debug file are kept readable.
            bool isProgress = line.Contains('%') ||
                              line.Contains("percent complete", StringComparison.OrdinalIgnoreCase) ||
                              line.Contains("已完成", StringComparison.Ordinal);
            long now = Environment.TickCount64;
            if (isProgress && now - lastProgressLogTicks < 500) return;
            if (isProgress) lastProgressLogTicks = now;

            DebugLogService.Instance.LogCommandOutput(category, line);
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (read <= 0) break;

                for (int i = 0; i < read; i++)
                {
                    char ch = buffer[i];

                    if (ch == '\n')
                    {
                        if (previousWasCarriageReturn)
                        {
                            previousWasCarriageReturn = false;
                            continue;
                        }
                        Emit(current.ToString());
                        current.Clear();
                        continue;
                    }

                    previousWasCarriageReturn = false;
                    if (ch == '\r')
                    {
                        Emit(current.ToString());
                        current.Clear();
                        previousWasCarriageReturn = true;
                    }
                    else if (ch == '\b')
                    {
                        if (current.Length > 0) current.Length--;
                    }
                    else if (ch != '\0')
                    {
                        current.Append(ch);
                    }

                    if (current.Length >= 4000)
                    {
                        Emit(current.ToString());
                        current.Clear();
                    }
                }

                // A Y/N prompt may not include a newline. Inspect the pending buffer as it
                // grows so the input responder can answer before the stream deadlocks.
                if (current.Length > 0)
                {
                    TryInvoke(onTextSeen, current.ToString());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"讀取程序輸出失敗: {ex.Message}", category);
        }

        if (current.Length > 0)
        {
            Emit(current.ToString());
        }
    }

    private static string Normalize(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return string.Empty;
        string value = AnsiRegex.Replace(raw, string.Empty);
        value = value.Replace('\0', ' ').TrimEnd();
        return value;
    }

    private static void TryInvoke(Action<string> callback, string line)
    {
        try { callback(line); }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"命令輸出的取用端發生例外: {ex.Message}", "Streaming");
        }
    }
}
