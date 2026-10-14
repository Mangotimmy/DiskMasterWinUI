using System.Collections.Concurrent;
using System.Text;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// High-throughput, thread-safe log buffer that throttles UI thread dispatches to 60 FPS
/// and enforces bounds on total log size to prevent UI stutter and excessive GC allocation.
/// The flush timer only runs while lines are queued, so an idle page costs nothing.
/// </summary>
public class ThrottledLogBuffer : IDisposable
{
    private readonly Action<string> _updateUiAction;
    private readonly ConcurrentQueue<string> _pendingLines = new();
    private readonly StringBuilder _buffer = new();
    private readonly Timer _timer;
    private readonly int _intervalMs;
    private readonly int _maxLines;
    private readonly int _maxChars;
    private int _lineCount = 0;
    private int _isFlushing = 0;
    private volatile bool _hasNewContent = false;
    private bool _disposed = false;

    public ThrottledLogBuffer(
        Action<string> updateUiAction,
        int intervalMs = 60,
        int maxLines = 5000,
        int maxChars = 250000)
    {
        _updateUiAction = updateUiAction ?? throw new ArgumentNullException(nameof(updateUiAction));
        _maxLines = maxLines;
        _maxChars = maxChars;
        _intervalMs = intervalMs;

        // Starts idle; Append() starts it. A permanently ticking 60 ms timer kept firing after
        // the owning page was gone and rooted the whole ViewModel graph through the callback.
        _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Thread-safe append of a string chunk to the log buffer.
    /// </summary>
    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text) || _disposed) return;
        _pendingLines.Enqueue(text);
        if (!_hasNewContent)
        {
            _hasNewContent = true;
            try { _timer.Change(_intervalMs, _intervalMs); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Thread-safe append of a line ending with newline to the log buffer.
    /// </summary>
    public void AppendLine(string line)
    {
        Append(line + "\n");
    }

    /// <summary>
    /// Thread-safe append of a formatted line with timestamp.
    /// </summary>
    public void AppendTimestampedLine(string line)
    {
        Append($"[{DateTime.Now:HH:mm:ss}] {line}\n");
    }

    /// <summary>
    /// Clears both pending queues and the buffered text, updating UI immediately.
    /// </summary>
    public void Clear()
    {
        while (_pendingLines.TryDequeue(out _)) { }
        lock (_buffer)
        {
            _buffer.Clear();
            _lineCount = 0;
        }
        _hasNewContent = false;
        DispatcherHelper.RunOnUIThread(() => _updateUiAction(string.Empty));
    }

    /// <summary>
    /// Immediately flushes all queued lines to the UI without waiting for timer interval.
    /// </summary>
    public void FlushNow()
    {
        FlushInternal();
    }

    private void OnTimerTick(object? state)
    {
        if (!_hasNewContent && _pendingLines.IsEmpty)
        {
            StopTimerIfIdle();
            return;
        }

        FlushInternal();
        StopTimerIfIdle();
    }

    /// <summary>
    /// Stops the periodic flush while nothing is queued, then re-checks: an Append on another
    /// thread can land between the check and the stop, and that line must not be stranded.
    /// </summary>
    private void StopTimerIfIdle()
    {
        if (_hasNewContent || !_pendingLines.IsEmpty) return;

        try { _timer.Change(Timeout.Infinite, Timeout.Infinite); }
        catch (ObjectDisposedException) { return; }

        if (_hasNewContent || !_pendingLines.IsEmpty)
        {
            try { _timer.Change(_intervalMs, _intervalMs); }
            catch (ObjectDisposedException) { }
        }
    }

    private void FlushInternal()
    {
        if (_disposed) return;
        if (Interlocked.Exchange(ref _isFlushing, 1) != 0) return;

        try
        {
            if (_pendingLines.IsEmpty) return;

            bool changed = false;
            lock (_buffer)
            {
                while (_pendingLines.TryDequeue(out var line))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    _buffer.Append(line);
                    _lineCount++;
                    changed = true;
                }

                if (changed)
                {
                    // Bound the buffer memory & line count
                    if (_buffer.Length > _maxChars || _lineCount > _maxLines)
                    {
                        var text = _buffer.ToString();
                        var cutPoint = text.Length > (_maxChars / 2) ? text.Length - (_maxChars / 2) : 0;
                        var nextLine = text.IndexOf('\n', cutPoint);
                        if (nextLine >= 0 && nextLine < text.Length - 1)
                        {
                            var truncated = text.Substring(nextLine + 1);
                            _buffer.Clear();
                            _buffer.Append("[... earlier log truncated for performance ...]\n");
                            _buffer.Append(truncated);
                            _lineCount = _maxLines / 2;
                        }
                    }
                }
            }

            if (changed)
            {
                string snapshot;
                lock (_buffer)
                {
                    snapshot = _buffer.ToString();
                }

                _hasNewContent = false;
                DispatcherHelper.RunOnUIThread(() =>
                {
                    _updateUiAction(snapshot);
                });
            }
        }
        catch { }
        finally
        {
            Interlocked.Exchange(ref _isFlushing, 0);
        }
    }

    public void Dispose()
    {
        // Flush before marking disposed: FlushInternal() returns immediately once _disposed is
        // set, so the previous order silently dropped the last queued lines — including the error
        // that triggered the shutdown.
        FlushInternal();
        _disposed = true;
        try { _timer.Dispose(); } catch { }
        while (_pendingLines.TryDequeue(out _)) { }
    }
}
