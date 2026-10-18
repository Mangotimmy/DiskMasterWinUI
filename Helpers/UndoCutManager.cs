using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DiskMasterWinUI.Pages;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Provides global and per-page Undo (Ctrl+Z) and Cut (Ctrl+X) support across all 14 pages,
/// both inside focused TextBox controls (including VS Code-style whole-box/line cut + undo)
/// and at the page level (cutting/restoring console logs, diagnostics, Hosts editor, and status).
/// </summary>
public static class UndoCutManager
{
    private sealed record UndoEntry(string Description, Action RestoreAction, object? TargetObject = null);

    private static readonly Stack<UndoEntry> _undoStack = new();
    private const int MaxUndoDepth = 50;

    /// <summary>
    /// Records a reversible action onto the global undo stack so Ctrl+Z can restore it.
    /// </summary>
    public static void PushUndo(string description, Action restoreAction)
    {
        PushUndoCore(description, restoreAction, targetObject: null);
    }

    private static void PushUndoCore(string description, Action restoreAction, object? targetObject)
    {
        if (restoreAction == null) return;
        if (_undoStack.Count >= MaxUndoDepth)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = Math.Min(items.Length - 1, MaxUndoDepth - 2); i >= 0; i--)
            {
                _undoStack.Push(items[i]);
            }
        }
        _undoStack.Push(new UndoEntry(description, restoreAction, targetObject));
        try
        {
            DebugLogService.Instance.LogAction("回復復原 (UndoCut)", $"註冊可復原動作: {description}");
        }
        catch { }
    }

    public static bool CanUndo => _undoStack.Count > 0;

    public static string LastStatusMessage { get; private set; } = string.Empty;

    public static string LastCutText { get; private set; } = string.Empty;

    /// <summary>
    /// Attaches tunneling PreviewKeyDown Ctrl+Z and Ctrl+X handlers to a window root element
    /// so every page hosted inside MainWindow (including split-dock panes) or FloatingTabWindow
    /// supports Undo and Cut even when a TextBox has focus.
    /// </summary>
    public static void Attach(UIElement rootElement, Func<Page?> activePageResolver)
    {
        if (rootElement == null) return;

        rootElement.PreviewKeyDown += (sender, e) =>
        {
            if (e.Key != VirtualKey.Z && e.Key != VirtualKey.X) return;

            var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
            bool isCtrlDown = (ctrlState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (!isCtrlDown) return;

            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool isShiftDown = (shiftState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isShiftDown) return;

            var altState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu);
            bool isAltDown = (altState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
            if (isAltDown) return;

            var xamlRoot = (sender as FrameworkElement)?.XamlRoot ?? rootElement.XamlRoot;
            var focused = xamlRoot != null ? FocusManager.GetFocusedElement(xamlRoot) : null;
            var activePage = ResolveActivePage(focused, activePageResolver);

            if (e.Key == VirtualKey.Z)
            {
                if (TryExecuteUndo(xamlRoot, activePage))
                {
                    e.Handled = true;
                }
            }
            else if (e.Key == VirtualKey.X)
            {
                if (TryExecuteCut(xamlRoot, activePage))
                {
                    e.Handled = true;
                }
            }
        };
    }

    private static Page? ResolveActivePage(object? focusedElement, Func<Page?>? activePageResolver)
    {
        try
        {
            var current = focusedElement as DependencyObject;
            while (current != null)
            {
                if (current is Page page)
                {
                    return page;
                }
                current = VisualTreeHelper.GetParent(current);
            }
        }
        catch { }

        return activePageResolver?.Invoke();
    }

    /// <summary>
    /// Executes Undo (Ctrl+Z):
    /// 1. If the top of the global undo stack targets the currently focused TextBox (e.g., from a programmatic whole-box Cut), restores it first.
    /// 2. If a focused editable TextBox has native Undo history, invokes TextBox.Undo().
    /// 3. Otherwise pops and restores the most recent entry from the global Undo stack.
    /// </summary>
    public static bool TryExecuteUndo(XamlRoot? xamlRoot, Page? activePage)
    {
        try
        {
            object? focused = xamlRoot != null ? FocusManager.GetFocusedElement(xamlRoot) : null;
            activePage = ResolveActivePage(focused, () => activePage);

            if (focused is TextBox tb)
            {
                if (_undoStack.TryPeek(out var topEntry) && ReferenceEquals(topEntry.TargetObject, tb))
                {
                    _undoStack.Pop();
                    topEntry.RestoreAction();
                    NotifyStatus(activePage, $"↩️ 已復原：{topEntry.Description} (Ctrl+Z)");
                    return true;
                }

                if (!tb.IsReadOnly && tb.CanUndo)
                {
                    tb.Undo();
                    NotifyStatus(activePage, "↩️ 已復原文字輸入 (Ctrl+Z)");
                    return true;
                }
            }

            if (_undoStack.TryPop(out var entry))
            {
                entry.RestoreAction();
                NotifyStatus(activePage, $"↩️ 已復原：{entry.Description} (Ctrl+Z)");
                return true;
            }

            NotifyStatus(activePage, "ℹ️ 目前沒有可復原的操作 (Ctrl+Z)");
            return false;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"Undo failed: {ex.Message}", "UndoCut");
            return false;
        }
    }

    /// <summary>
    /// Executes Cut (Ctrl+X):
    /// 1. If a TextBox is focused:
    ///    - Editable with selection -> cuts selected text and pushes undo backup.
    ///    - Editable without selection -> cuts entire TextBox text to clipboard and pushes undo backup.
    ///    - ReadOnly with selection -> copies selected text to clipboard.
    /// 2. Otherwise performs page-level Cut on the active page's log/output/status and pushes undo backup.
    /// </summary>
    public static bool TryExecuteCut(XamlRoot? xamlRoot, Page? activePage)
    {
        try
        {
            object? focused = xamlRoot != null ? FocusManager.GetFocusedElement(xamlRoot) : null;
            activePage = ResolveActivePage(focused, () => activePage);

            if (focused is TextBox tb)
            {
                if (!tb.IsReadOnly)
                {
                    string oldText = tb.Text ?? string.Empty;
                    if (!string.IsNullOrEmpty(oldText))
                    {
                        if (tb.SelectionLength > 0)
                        {
                            string selected = tb.SelectedText;
                            int selStart = tb.SelectionStart;
                            CopyTextToClipboard(selected);
                            PushUndoCore("剪下文字選取內容", () =>
                            {
                                tb.Text = oldText;
                                tb.SelectionStart = Math.Min(selStart, tb.Text.Length);
                            }, tb);
                            tb.SelectedText = string.Empty;
                            NotifyStatus(activePage, "✂️ 已剪下選取文字至剪貼簿 (可按 Ctrl+Z 復原)");
                            return true;
                        }
                        else
                        {
                            CopyTextToClipboard(oldText);
                            PushUndoCore("剪下輸入框內容", () =>
                            {
                                tb.Text = oldText;
                                tb.SelectionStart = tb.Text.Length;
                            }, tb);
                            tb.Text = string.Empty;
                            NotifyStatus(activePage, "✂️ 已剪下輸入框全部內容至剪貼簿 (可按 Ctrl+Z 復原)");
                            return true;
                        }
                    }
                }
                else if (tb.SelectionLength > 0)
                {
                    CopyTextToClipboard(tb.SelectedText);
                    NotifyStatus(activePage, "✂️ 已將選取內容複製至剪貼簿");
                    return true;
                }
            }

            if (activePage != null && TryCutPageContent(activePage))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugLogService.Instance.Warning($"Cut failed: {ex.Message}", "UndoCut");
            return false;
        }
    }

    private static bool TryCutPageContent(Page page)
    {
        switch (page)
        {
            case EasyModePage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.OutputLog))
                {
                    var snapshot = p.ViewModel.OutputLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("簡易模式執行紀錄", () => p.ViewModel.OutputLog = snapshot);
                    p.ViewModel.OutputLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下簡易模式紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("簡易模式狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下簡易模式狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case WimDeployPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.DeployLog))
                {
                    var snapshot = p.ViewModel.DeployLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("WIM 部署紀錄", () => p.ViewModel.DeployLog = snapshot);
                    p.ViewModel.DeployLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下 WIM 部署紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("WIM 部署狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下 WIM 部署狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case BootManagerPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.NewBootLog))
                {
                    var snapshot = p.ViewModel.NewBootLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("BCD 引導輸出紀錄", () => p.ViewModel.NewBootLog = snapshot);
                    p.ViewModel.NewBootLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下 BCD 引導輸出至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("開機管理狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下開機管理狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case DiskToolsPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.OutputLog))
                {
                    var snapshot = p.ViewModel.OutputLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("磁碟工具輸出紀錄", () => p.ViewModel.OutputLog = snapshot);
                    p.ViewModel.OutputLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下磁碟工具紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("磁碟工具狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下磁碟工具狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case RecoveryPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.TerminalOutput))
                {
                    var snapshot = p.ViewModel.TerminalOutput;
                    CopyTextToClipboard(snapshot);
                    PushUndo("檔案復原輸出紀錄", () => p.ViewModel.TerminalOutput = snapshot);
                    p.ViewModel.TerminalOutput = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下檔案復原紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("檔案復原狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下檔案復原狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case SystemHealthPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.TerminalOutput))
                {
                    var snapshot = p.ViewModel.TerminalOutput;
                    CopyTextToClipboard(snapshot);
                    PushUndo("系統健康修復紀錄", () => p.ViewModel.TerminalOutput = snapshot);
                    p.ViewModel.TerminalOutput = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下系統修復紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.CurrentStatus))
                {
                    var statusSnap = p.ViewModel.CurrentStatus;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("系統修復狀態訊息", () => p.ViewModel.CurrentStatus = statusSnap);
                    p.ViewModel.CurrentStatus = "✂️ 已剪下系統修復狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case NtfsPermissionsPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.TerminalOutput))
                {
                    var snapshot = p.ViewModel.TerminalOutput;
                    CopyTextToClipboard(snapshot);
                    PushUndo("NTFS 權限操作紀錄", () => p.ViewModel.TerminalOutput = snapshot);
                    p.ViewModel.TerminalOutput = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下 NTFS 權限紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("NTFS 權限狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下 NTFS 權限狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case GroupPolicyPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.ConsoleLog))
                {
                    var snapshot = p.ViewModel.ConsoleLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("群組原則操作紀錄", () => p.ViewModel.ConsoleLog = snapshot);
                    p.ViewModel.ConsoleLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下群組原則紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("群組原則狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下群組原則狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case AdvancedModePage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.FullLog))
                {
                    var snapshot = p.ViewModel.FullLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("進階模式完整紀錄", () => p.ViewModel.FullLog = snapshot);
                    p.ViewModel.ClearFullLogCommand.Execute(null);
                    NotifyStatus(page, "✂️ 已剪下進階模式紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("進階模式狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下進階模式狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case SystemOptimizerPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.ConsoleLog))
                {
                    var snapshot = p.ViewModel.ConsoleLog;
                    CopyTextToClipboard(snapshot);
                    PushUndo("系統優化主控台紀錄", () => p.ViewModel.ConsoleLog = snapshot);
                    p.ViewModel.ConsoleLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下系統優化紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("系統優化狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下系統優化狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case NetworkToolsPage p:
            {
                if (!string.IsNullOrEmpty(p.ViewModel.PingOutputLog))
                {
                    var pingSnap = p.ViewModel.PingOutputLog;
                    CopyTextToClipboard(pingSnap);
                    PushUndo("網路 Ping 輸出紀錄", () => p.ViewModel.PingOutputLog = pingSnap);
                    p.ViewModel.PingOutputLog = string.Empty;
                    NotifyStatus(page, "✂️ 已剪下網路 Ping 紀錄至剪貼簿 (按 Ctrl+Z 可復原)");
                    return true;
                }
                if (!string.IsNullOrEmpty(p.ViewModel.StatusMessage))
                {
                    var statusSnap = p.ViewModel.StatusMessage;
                    CopyTextToClipboard(statusSnap);
                    PushUndo("網路工具狀態訊息", () => p.ViewModel.StatusMessage = statusSnap);
                    p.ViewModel.StatusMessage = "✂️ 已剪下網路狀態訊息至剪貼簿 (按 Ctrl+Z 可復原)";
                    return true;
                }
                break;
            }
            case SystemPerformancePage p when !string.IsNullOrEmpty(p.ViewModel.StatusMessage):
            {
                var snapshot = p.ViewModel.StatusMessage;
                CopyTextToClipboard(snapshot);
                PushUndo("效能分析狀態訊息", () => p.ViewModel.StatusMessage = snapshot);
                p.ViewModel.StatusMessage = "✂️ 已剪下效能分析狀態至剪貼簿 (按 Ctrl+Z 可復原)";
                return true;
            }
            case SettingsPage p when !string.IsNullOrEmpty(p.ViewModel.StatusMessage):
            {
                var snapshot = p.ViewModel.StatusMessage;
                CopyTextToClipboard(snapshot);
                PushUndo("設定頁面狀態訊息", () => p.ViewModel.StatusMessage = snapshot);
                p.ViewModel.StatusMessage = "✂️ 已剪下設定狀態訊息至剪貼簿 (按 Ctrl+Z 可復原)";
                return true;
            }
            case StarterPage p when !string.IsNullOrEmpty(p.ViewModel.OperationStatus):
            {
                var currentStatus = p.ViewModel.OperationStatus;
                CopyTextToClipboard(currentStatus);
                PushUndo("守護精靈狀態訊息", () => p.ViewModel.OperationStatus = currentStatus);
                p.ViewModel.OperationStatus = "✂️ 已剪下狀態訊息至剪貼簿 (按 Ctrl+Z 可復原)";
                return true;
            }
        }
        return false;
    }

    private static void NotifyStatus(Page? page, string message)
    {
        LastStatusMessage = message;
        DebugLogService.Instance.Info(message, "UndoCut");
        switch (page)
        {
            case StarterPage p: p.ViewModel.OperationStatus = message; break;
            case EasyModePage p: p.ViewModel.StatusMessage = message; break;
            case WimDeployPage p: p.ViewModel.StatusMessage = message; break;
            case BootManagerPage p: p.ViewModel.StatusMessage = message; break;
            case DiskToolsPage p: p.ViewModel.StatusMessage = message; break;
            case RecoveryPage p: p.ViewModel.StatusMessage = message; break;
            case SystemHealthPage p: p.ViewModel.CurrentStatus = message; break;
            case NtfsPermissionsPage p: p.ViewModel.StatusMessage = message; break;
            case GroupPolicyPage p: p.ViewModel.StatusMessage = message; break;
            case AdvancedModePage p: p.ViewModel.StatusMessage = message; break;
            case SystemOptimizerPage p: p.ViewModel.StatusMessage = message; break;
            case NetworkToolsPage p: p.ViewModel.StatusMessage = message; break;
            case SystemPerformancePage p: p.ViewModel.StatusMessage = message; break;
            case SettingsPage p: p.ViewModel.StatusMessage = message; break;
        }
    }

    private static void CopyTextToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        LastCutText = text;
        try
        {
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }
        catch { }
    }
}
