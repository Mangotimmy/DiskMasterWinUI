using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DiskMasterWinUI.Helpers;

/// <summary>
/// Recursively manages and propagates font family and font size scaling across all pages,
/// controls, and windows without accumulative sizing drift.
/// </summary>
public static class FontScaleHelper
{
    public static readonly DependencyProperty OriginalFontSizeProperty =
        DependencyProperty.RegisterAttached(
            "OriginalFontSize",
            typeof(double),
            typeof(FontScaleHelper),
            new PropertyMetadata(0.0));

    public static double GetOriginalFontSize(DependencyObject obj) =>
        (double)obj.GetValue(OriginalFontSizeProperty);

    public static void SetOriginalFontSize(DependencyObject obj, double value) =>
        obj.SetValue(OriginalFontSizeProperty, value);

    private static readonly DependencyProperty IsListeningLoadedProperty =
        DependencyProperty.RegisterAttached(
            "IsListeningLoaded",
            typeof(bool),
            typeof(FontScaleHelper),
            new PropertyMetadata(false));

    private static bool GetIsListeningLoaded(DependencyObject obj) =>
        (bool)obj.GetValue(IsListeningLoadedProperty);

    private static void SetIsListeningLoaded(DependencyObject obj, bool value) =>
        obj.SetValue(IsListeningLoadedProperty, value);

    /// <summary>
    /// Applies the specified font family and scale factor to the root element and all its visual and logical children.
    /// If an element is not yet loaded, a one-shot Loaded handler is attached to ensure proper traversal when it renders.
    /// </summary>
    public static void ApplyFontAndScale(DependencyObject? root, FontFamily? fontFamily, double scaleFactor)
    {
        if (root == null) return;
        scaleFactor = Math.Clamp(scaleFactor, 0.5, 3.0);
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        TraverseAndApply(root, fontFamily, scaleFactor, visited);
    }

    private static void TraverseAndApply(DependencyObject root, FontFamily? fontFamily, double scaleFactor, HashSet<object> visited)
    {
        if (!visited.Add(root)) return;

        if (root is FrameworkElement fe && !fe.IsLoaded && !GetIsListeningLoaded(fe))
        {
            SetIsListeningLoaded(fe, true);
            RoutedEventHandler? handler = null;
            handler = (s, e) =>
            {
                fe.Loaded -= handler;
                SetIsListeningLoaded(fe, false);
                var fontName = Services.SettingsService.Instance.Current.CustomFontFamily;
                if (string.IsNullOrWhiteSpace(fontName)) fontName = "Segoe UI Variable";
                var scale = Services.SettingsService.Instance.Current.CustomFontScale > 0 ? Services.SettingsService.Instance.Current.CustomFontScale : 100.0;
                ApplyFontAndScale(fe, new FontFamily(fontName), scale / 100.0);
            };
            fe.Loaded += handler;
        }

        ApplyToElement(root, fontFamily, scaleFactor);

        // 1. Visual tree traversal
        try
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child != null)
                {
                    TraverseAndApply(child, fontFamily, scaleFactor, visited);
                }
            }
        }
        catch { }

        // 2. Logical and structural tree traversal (critical for unparented/unloaded Pages, UserControls & Controls)
        if (root is UserControl uc && uc.Content is DependencyObject ucContent)
        {
            TraverseAndApply(ucContent, fontFamily, scaleFactor, visited);
        }
        else if (root is ContentControl cc && cc.Content is DependencyObject ccContent)
        {
            TraverseAndApply(ccContent, fontFamily, scaleFactor, visited);
        }
        else if (root is Border border && border.Child is DependencyObject borderChild)
        {
            TraverseAndApply(borderChild, fontFamily, scaleFactor, visited);
        }
        else if (root is Viewbox vb && vb.Child is DependencyObject vbChild)
        {
            TraverseAndApply(vbChild, fontFamily, scaleFactor, visited);
        }
        else if (root is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child != null)
                {
                    TraverseAndApply(child, fontFamily, scaleFactor, visited);
                }
            }
        }
        else if (root is Expander exp)
        {
            if (exp.Header is DependencyObject expHeader) TraverseAndApply(expHeader, fontFamily, scaleFactor, visited);
            if (exp.Content is DependencyObject expContent) TraverseAndApply(expContent, fontFamily, scaleFactor, visited);
        }
        else if (root is SplitView sv)
        {
            if (sv.Pane is DependencyObject svPane) TraverseAndApply(svPane, fontFamily, scaleFactor, visited);
            if (sv.Content is DependencyObject svContent) TraverseAndApply(svContent, fontFamily, scaleFactor, visited);
        }
        else if (root is ItemsControl ic)
        {
            foreach (var item in ic.Items)
            {
                if (item is DependencyObject itemDep)
                {
                    TraverseAndApply(itemDep, fontFamily, scaleFactor, visited);
                }
            }
        }
    }

    private static void ApplyToElement(DependencyObject obj, FontFamily? fontFamily, double scaleFactor)
    {
        if (obj is TextBlock tb)
        {
            double orig = GetOriginalFontSize(tb);
            if (orig <= 0)
            {
                orig = tb.FontSize > 0 ? tb.FontSize : 14.0;
                SetOriginalFontSize(tb, orig);
            }
            tb.FontSize = Math.Round(orig * scaleFactor, 1);

            if (fontFamily != null && ShouldApplyFont(tb.FontFamily))
            {
                tb.FontFamily = fontFamily;
            }
        }
        else if (obj is FontIcon fi)
        {
            double orig = GetOriginalFontSize(fi);
            if (orig <= 0)
            {
                orig = fi.FontSize > 0 ? fi.FontSize : 14.0;
                SetOriginalFontSize(fi, orig);
            }
            fi.FontSize = Math.Round(orig * scaleFactor, 1);
            // Never overwrite FontIcon.FontFamily (needed for Segoe Fluent Icons / glyphs)
        }
        else if (obj is Control ctrl)
        {
            double orig = GetOriginalFontSize(ctrl);
            if (orig <= 0)
            {
                orig = ctrl.FontSize > 0 ? ctrl.FontSize : 14.0;
                SetOriginalFontSize(ctrl, orig);
            }
            ctrl.FontSize = Math.Round(orig * scaleFactor, 1);

            if (fontFamily != null && ShouldApplyFont(ctrl.FontFamily))
            {
                ctrl.FontFamily = fontFamily;
            }
        }
        else if (obj is RichTextBlock rtb)
        {
            double orig = GetOriginalFontSize(rtb);
            if (orig <= 0)
            {
                orig = rtb.FontSize > 0 ? rtb.FontSize : 14.0;
                SetOriginalFontSize(rtb, orig);
            }
            rtb.FontSize = Math.Round(orig * scaleFactor, 1);

            if (fontFamily != null && ShouldApplyFont(rtb.FontFamily))
            {
                rtb.FontFamily = fontFamily;
            }
        }
    }

    private static bool ShouldApplyFont(FontFamily? family)
    {
        if (family == null || string.IsNullOrWhiteSpace(family.Source)) return true;
        var src = family.Source;
        if (src.Contains("Icon", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("MDL2", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            src.Contains("Courier", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }
}
