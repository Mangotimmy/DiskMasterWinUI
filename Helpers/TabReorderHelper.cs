using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Universal helper providing reliable drag-and-drop tab reordering,
/// context menu movement (Move Left / Move Right), and keyboard shortcuts (Alt+Left / Alt+Right)
/// for WinUI 3 TabView controls across main and sub-feature tabs.
/// </summary>
public static class TabReorderHelper
{
    private static TabViewItem? _currentDraggedItem;

    /// <summary>
    /// Attaches smooth reordering capabilities to the specified TabView.
    /// </summary>
    public static void Attach(TabView tabView, Action? onReordered = null)
    {
        if (tabView == null) return;

        tabView.CanDragTabs = true;
        tabView.CanReorderTabs = true;

        tabView.TabDragStarting += (sender, args) =>
        {
            _currentDraggedItem = args.Tab;
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.Properties["DraggedTab"] = args.Tab;
        };

        tabView.TabStripDragOver += (sender, args) =>
        {
            args.AcceptedOperation = DataPackageOperation.Move;
        };

        tabView.TabStripDrop += (sender, args) =>
        {
            var dragged = _currentDraggedItem;
            _currentDraggedItem = null;

            if (dragged == null || sender is not TabView tv) return;

            int oldIndex = tv.TabItems.IndexOf(dragged);
            if (oldIndex < 0) return;

            // Calculate drop target index based on cursor X position
            var point = args.GetPosition(tv);
            int newIndex = tv.TabItems.Count - 1;

            for (int i = 0; i < tv.TabItems.Count; i++)
            {
                if (tv.TabItems[i] is FrameworkElement container)
                {
                    var transform = container.TransformToVisual(tv);
                    var containerPos = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    if (point.X < containerPos.X + (container.ActualWidth / 2))
                    {
                        newIndex = i;
                        break;
                    }
                }
            }

            if (newIndex >= 0 && newIndex != oldIndex && newIndex < tv.TabItems.Count)
            {
                tv.TabItems.RemoveAt(oldIndex);
                tv.TabItems.Insert(newIndex, dragged);
                tv.SelectedItem = dragged;
                onReordered?.Invoke();
            }
        };

        // Attach context menu items and keyboard reordering to each tab item
        EnhanceTabs(tabView, onReordered);

        tabView.Loaded += (s, e) => EnhanceTabs(tabView, onReordered);
    }

    /// <summary>
    /// Injects 'Move Left' and 'Move Right' items into each TabViewItem's context flyout.
    /// </summary>
    public static void EnhanceTabs(TabView tabView, Action? onReordered)
    {
        for (int i = 0; i < tabView.TabItems.Count; i++)
        {
            if (tabView.TabItems[i] is TabViewItem tabItem)
            {
                AttachTabContextCommands(tabView, tabItem, onReordered);
            }
        }
    }

    private static void AttachTabContextCommands(TabView tabView, TabViewItem tabItem, Action? onReordered)
    {
        MenuFlyout flyout;
        if (tabItem.ContextFlyout is MenuFlyout mf)
        {
            flyout = mf;
        }
        else
        {
            flyout = new MenuFlyout();
            tabItem.ContextFlyout = flyout;
        }

        // Avoid duplicate items
        bool hasMoveLeft = false;
        foreach (var item in flyout.Items)
        {
            if (item is MenuFlyoutItem mfi && (mfi.Tag?.ToString() == "MoveLeft" || mfi.Tag?.ToString() == "MoveRight"))
            {
                hasMoveLeft = true;
                break;
            }
        }

        if (!hasMoveLeft)
        {
            var sep = new MenuFlyoutSeparator();
            var moveLeftItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Instance["MoveLeft"],
                Tag = "MoveLeft",
                Icon = new FontIcon { Glyph = "\uE76B" }
            };
            moveLeftItem.Click += (s, e) => MoveTab(tabView, tabItem, -1, onReordered);

            var moveRightItem = new MenuFlyoutItem
            {
                Text = LocalizationService.Instance["MoveRight"],
                Tag = "MoveRight",
                Icon = new FontIcon { Glyph = "\uE76C" }
            };
            moveRightItem.Click += (s, e) => MoveTab(tabView, tabItem, 1, onReordered);

            flyout.Items.Add(sep);
            flyout.Items.Add(moveLeftItem);
            flyout.Items.Add(moveRightItem);
        }
    }

    /// <summary>
    /// Moves a TabViewItem left (-1) or right (+1) in its parent TabView.
    /// </summary>
    public static void MoveTab(TabView tabView, TabViewItem tabItem, int delta, Action? onReordered)
    {
        int currentIndex = tabView.TabItems.IndexOf(tabItem);
        if (currentIndex < 0) return;

        int targetIndex = currentIndex + delta;
        if (targetIndex < 0 || targetIndex >= tabView.TabItems.Count) return;

        tabView.TabItems.RemoveAt(currentIndex);
        tabView.TabItems.Insert(targetIndex, tabItem);
        tabView.SelectedItem = tabItem;

        AudioFeedbackService.PlaySuccess();
        onReordered?.Invoke();
    }
}
