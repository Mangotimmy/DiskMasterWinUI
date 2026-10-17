using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

public enum CompanionEmotion
{
    Happy,
    Caution,
    Alert,
    Working
}

public sealed partial class AvatarCompanionControl : UserControl
{
    private DispatcherTimer? _breathTimer;
    private DispatcherTimer? _blinkTimer;
    private DispatcherTimer? _blinkAnimTimer;
    private DispatcherTimer? _bubbleTimer;

    private double _breathAngle;
    private int _blinkStep;
    private bool _isClosingEye;
    private readonly Random _rand = new();

    // Dragging state
    private bool _isDragging;
    private Point _dragStartPointerPos;
    private Point _dragStartTranslatePos;
    private bool _hasDragged;

    private readonly string[] _healthyQuotes =
    [
        "磁碟狀態良好，S.M.A.R.T. 指標 100% 正常！",
        "溫度很涼爽喔～今天也是系統順暢運作的一天！",
        "全透明守護中！開機引導與分區安全交給我～",
        "需要下載微軟官方原版 Windows 11 嗎？隨時準備為您加速！",
        "點擊我可以查看更多磁碟健康資訊喔～"
    ];

    private readonly string[] _cautionQuotes =
    [
        "注意喔！偵測到溫度較高或有潛在重定位磁區～",
        "建議重要檔案提早備份到外接硬碟或雲端喔！",
        "硬碟正在警戒溫度，請確認散熱與風道是否通暢。"
    ];

    private readonly string[] _alertQuotes =
    [
        "🚨 危險！磁碟健康度出現異常警訊！",
        "請立即停止高負載讀寫，盡速使用 WIM 備份重要分區！",
        "磁碟即將故障，強烈建議更換全新 SSD。"
    ];

    public AvatarCompanionControl()
    {
        InitializeComponent();
        this.Unloaded += AvatarCompanionControl_Unloaded;
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        StartBreathing();
        StartBlinking();
        ApplySavedSettings();

        if (this.Parent is FrameworkElement parentFe)
        {
            parentFe.SizeChanged -= ParentFe_SizeChanged;
            parentFe.SizeChanged += ParentFe_SizeChanged;
        }

        // Initial welcome speech after a brief delay
        var initTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
        initTimer.Tick += (_, _) =>
        {
            initTimer.Stop();
            Say("歡迎使用 DiskMaster！我是您的專屬磁碟守護伴侶～", 4500);
        };
        initTimer.Start();
    }

    private void ParentFe_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        AutoDetectAndSnapToEdge();
    }

    private void AvatarCompanionControl_Unloaded(object sender, RoutedEventArgs e)
    {
        if (this.Parent is FrameworkElement parentFe)
        {
            parentFe.SizeChanged -= ParentFe_SizeChanged;
        }
        _breathTimer?.Stop();
        _blinkTimer?.Stop();
        _blinkAnimTimer?.Stop();
        _bubbleTimer?.Stop();
    }

    public void ApplySavedSettings()
    {
        var settings = SettingsService.Instance.Current;
        SetScale(Math.Clamp(settings.CompanionScale, 0.5, 1.8), save: false);

        if (settings.CompanionPosition == "Free")
        {
            AvatarPositionTransform.X = settings.CompanionOffsetX;
            AvatarPositionTransform.Y = settings.CompanionOffsetY;
            AutoDetectAndSnapToEdge();
        }
        else
        {
            SetDockPosition(settings.CompanionPosition, save: false);
        }

        HealthBadgeBorder.Visibility = settings.CompanionShowBadge ? Visibility.Visible : Visibility.Collapsed;
        MenuToggleBadge.IsChecked = settings.CompanionShowBadge;
        MenuToggleGaze.IsChecked = settings.CompanionEnableGazeTracking;
    }

    // ── Breathing Animation ──
    private void StartBreathing()
    {
        _breathTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _breathTimer.Tick += (_, _) =>
        {
            _breathAngle += 0.05;
            if (_breathAngle > Math.PI * 2) _breathAngle -= Math.PI * 2;
            BreathTransform.Y = Math.Sin(_breathAngle) * 3.0;
        };
        _breathTimer.Start();
    }

    // ── Blinking Animation ──
    private void StartBlinking()
    {
        ScheduleNextBlink();
    }

    private void ScheduleNextBlink()
    {
        _blinkTimer?.Stop();
        var nextInterval = _rand.Next(2500, 5500);
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(nextInterval) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkTimer.Stop();
            TriggerBlink();
            ScheduleNextBlink();
        };
        _blinkTimer.Start();
    }

    private void TriggerBlink()
    {
        _blinkStep = 0;
        _isClosingEye = true;
        _blinkAnimTimer?.Stop();
        _blinkAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        _blinkAnimTimer.Tick += (_, _) =>
        {
            if (_isClosingEye)
            {
                _blinkStep++;
                var scale = Math.Max(0.1, 1.0 - (_blinkStep * 0.35));
                LeftEyeBlinkTransform.ScaleY = scale;
                RightEyeBlinkTransform.ScaleY = scale;
                if (scale <= 0.15)
                {
                    _isClosingEye = false;
                }
            }
            else
            {
                _blinkStep--;
                var scale = Math.Min(1.0, 0.15 + ((3 - _blinkStep) * 0.35));
                LeftEyeBlinkTransform.ScaleY = scale;
                RightEyeBlinkTransform.ScaleY = scale;
                if (scale >= 0.95)
                {
                    LeftEyeBlinkTransform.ScaleY = 1.0;
                    RightEyeBlinkTransform.ScaleY = 1.0;
                    _blinkAnimTimer.Stop();
                }
            }
        };
        _blinkAnimTimer.Start();
    }

    // ── Speech Bubble ──
    public void Say(string text, int durationMs = 4000)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SpeechText.Text = text;
            SpeechBubble.Opacity = 1.0;

            _bubbleTimer?.Stop();
            _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _bubbleTimer.Tick += (_, _) =>
            {
                _bubbleTimer.Stop();
                SpeechBubble.Opacity = 0.0;
            };
            _bubbleTimer.Start();
        });
    }

    // ── CrystalDiskInfo Health & Temp Badge ──
    public void UpdateHealthStatus(string healthStatus, int? temperature)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var isHealthy = healthStatus.Contains("Healthy", StringComparison.OrdinalIgnoreCase) ||
                            healthStatus.Contains("Good", StringComparison.OrdinalIgnoreCase) ||
                            healthStatus.Contains("良好");

            var isWarning = healthStatus.Contains("Warning", StringComparison.OrdinalIgnoreCase) ||
                            healthStatus.Contains("Caution", StringComparison.OrdinalIgnoreCase) ||
                            healthStatus.Contains("注意");

            var isAlert = healthStatus.Contains("Unhealthy", StringComparison.OrdinalIgnoreCase) ||
                          healthStatus.Contains("Bad", StringComparison.OrdinalIgnoreCase) ||
                          healthStatus.Contains("異常") ||
                          healthStatus.Contains("危險");

            var tempDisplay = temperature.HasValue ? $"{temperature.Value}°C" : "32°C";
            HealthBadgeTemp.Text = tempDisplay;

            if (isAlert)
            {
                HealthBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(255, 232, 17, 35)); // #E81123
                HealthBadgeIcon.Text = "🔴";
                HealthBadgeText.Text = "異常 0%";
                if (SettingsService.Instance.Current.CompanionEmotionState == "Auto")
                {
                    SetEmotion(CompanionEmotion.Alert);
                }
            }
            else if (isWarning)
            {
                HealthBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(255, 216, 59, 1)); // #D83B01
                HealthBadgeIcon.Text = "🟡";
                HealthBadgeText.Text = "注意";
                if (SettingsService.Instance.Current.CompanionEmotionState == "Auto")
                {
                    SetEmotion(CompanionEmotion.Caution);
                }
            }
            else
            {
                HealthBadgeBorder.Background = new SolidColorBrush(Color.FromArgb(255, 16, 124, 65)); // #107C41
                HealthBadgeIcon.Text = "🟢";
                HealthBadgeText.Text = "良好 100%";
                if (SettingsService.Instance.Current.CompanionEmotionState == "Auto")
                {
                    SetEmotion(CompanionEmotion.Happy);
                }
            }
        });
    }

    // ── Emotion Morphing ──
    public void SetEmotion(CompanionEmotion emotion)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            NormalMouth.Visibility = Visibility.Collapsed;
            WorriedMouth.Visibility = Visibility.Collapsed;
            AlertMouth.Visibility = Visibility.Collapsed;
            SweatDropCanvas.Visibility = Visibility.Collapsed;
            AlarmSignBadge.Visibility = Visibility.Collapsed;
            WorkingSignBadge.Visibility = Visibility.Collapsed;

            switch (emotion)
            {
                case CompanionEmotion.Happy:
                    NormalMouth.Visibility = Visibility.Visible;
                    break;
                case CompanionEmotion.Caution:
                    WorriedMouth.Visibility = Visibility.Visible;
                    SweatDropCanvas.Visibility = Visibility.Visible;
                    break;
                case CompanionEmotion.Alert:
                    AlertMouth.Visibility = Visibility.Visible;
                    AlarmSignBadge.Visibility = Visibility.Visible;
                    break;
                case CompanionEmotion.Working:
                    NormalMouth.Visibility = Visibility.Visible;
                    WorkingSignBadge.Visibility = Visibility.Visible;
                    break;
            }
        });
    }

    // ── Pupil Gaze Tracking ──
    public void TrackPointer(Point windowPos)
    {
        if (!SettingsService.Instance.Current.CompanionEnableGazeTracking) return;

        try
        {
            var transform = this.TransformToVisual(null);
            var myPos = transform.TransformPoint(new Point(140, 180));

            var dx = (windowPos.X - myPos.X) * 0.035;
            var dy = (windowPos.Y - myPos.Y) * 0.035;

            var targetX = Math.Clamp(dx, -7.0, 7.0);
            var targetY = Math.Clamp(dy, -5.0, 5.0);

            LeftPupilTransform.X += (targetX - LeftPupilTransform.X) * 0.25;
            LeftPupilTransform.Y += (targetY - LeftPupilTransform.Y) * 0.25;
            RightPupilTransform.X += (targetX - RightPupilTransform.X) * 0.25;
            RightPupilTransform.Y += (targetY - RightPupilTransform.Y) * 0.25;
        }
        catch { }
    }

    // ── Free Drag & Drop Implementation ──
    private void UserControl_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var ptr = e.GetCurrentPoint(this);
        if (ptr.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _hasDragged = false;
            _dragStartPointerPos = e.GetCurrentPoint(this.Parent as UIElement).Position;
            _dragStartTranslatePos = new Point(AvatarPositionTransform.X, AvatarPositionTransform.Y);
            this.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void UserControl_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging && this.Parent is UIElement parentElement)
        {
            var currentPos = e.GetCurrentPoint(parentElement).Position;
            var deltaX = currentPos.X - _dragStartPointerPos.X;
            var deltaY = currentPos.Y - _dragStartPointerPos.Y;

            if (Math.Abs(deltaX) > 3 || Math.Abs(deltaY) > 3)
            {
                _hasDragged = true;
            }

            AvatarPositionTransform.X = _dragStartTranslatePos.X + deltaX;
            AvatarPositionTransform.Y = _dragStartTranslatePos.Y + deltaY;
            e.Handled = true;
        }
        else
        {
            var pt = e.GetCurrentPoint(this).Position;
            var dx = (pt.X - 140) * 0.06;
            var dy = (pt.Y - 140) * 0.06;

            LeftPupilTransform.X = Math.Clamp(dx, -7.0, 7.0);
            LeftPupilTransform.Y = Math.Clamp(dy, -5.0, 5.0);
            RightPupilTransform.X = Math.Clamp(dx, -7.0, 7.0);
            RightPupilTransform.Y = Math.Clamp(dy, -5.0, 5.0);
        }
    }

    private void UserControl_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            this.ReleasePointerCapture(e.Pointer);

            if (_hasDragged)
            {
                SnapDraggedPositionToNearestEdge(announce: true);
            }
            else
            {
                // Click without drag -> Say random quote
                PlayRandomQuote();
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Automatically detects the 2D helper's visual position in the parent container
    /// and snaps it cleanly to the Left or Right edge so it never blocks central UI content.
    /// </summary>
    public void SnapDraggedPositionToNearestEdge(bool announce = false)
    {
        if (this.Parent is not FrameworkElement parentFe || parentFe.ActualWidth <= 0 || parentFe.ActualHeight <= 0)
        {
            var s = SettingsService.Instance.Current;
            s.CompanionPosition = "BottomRight";
            s.CompanionOffsetX = 0;
            s.CompanionOffsetY = 0;
            SettingsService.Instance.Save();
            return;
        }

        try
        {
            var transform = this.TransformToVisual(parentFe);
            var centerPt = transform.TransformPoint(new Point(this.ActualWidth / 2.0, this.ActualHeight / 2.0));

            string targetDock = ResolveTargetDockEdge(centerPt.X, centerPt.Y, parentFe.ActualWidth, parentFe.ActualHeight);

            SetDockPosition(targetDock, save: true);

            if (announce)
            {
                bool snapLeft = targetDock.EndsWith("Left", StringComparison.Ordinal);
                var sideLabel = snapLeft ? "左側邊緣" : "右側邊緣";
                Say($"📌 已自動感應並貼齊停靠至視窗{sideLabel}！", 2500);
            }
        }
        catch
        {
            SetDockPosition("BottomRight", save: true);
        }
    }

    /// <summary>
    /// Pure geometric resolver that determines whether the 2D helper should dock to
    /// TopLeft, BottomLeft, TopRight, or BottomRight based on its visual center and container bounds.
    /// </summary>
    public static string ResolveTargetDockEdge(double centerX, double centerY, double containerWidth, double containerHeight, bool isRightSplitOpen = false)
    {
        if (containerWidth <= 0 || containerHeight <= 0)
        {
            return isRightSplitOpen ? "BottomLeft" : "BottomRight";
        }

        bool snapLeft = isRightSplitOpen || (centerX < (containerWidth / 2.0));
        bool snapTop = centerY >= 0 && centerY < (containerHeight * 0.42);

        return (snapLeft, snapTop) switch
        {
            (true, true) => "TopLeft",
            (true, false) => "BottomLeft",
            (false, true) => "TopRight",
            _ => "BottomRight"
        };
    }

    /// <summary>
    /// Automatically detects window bounds, DPI/compact state, and active split-screen docks
    /// to keep the 2D helper cleanly docked on the left or right edge without overlapping key content.
    /// </summary>
    public void AutoDetectAndSnapToEdge(bool isRightSplitOpen = false, bool announce = false)
    {
        var s = SettingsService.Instance.Current;
        var pos = s.CompanionPosition;

        if (this.Parent is FrameworkElement parentFe && parentFe.ActualWidth > 0)
        {
            // Automatically scale down slightly on narrow windows so the companion never crowds the page
            double baseScale = Math.Clamp(s.CompanionScale, 0.5, 1.8);
            double effectiveScale = parentFe.ActualWidth < 1050
                ? Math.Min(baseScale, 0.80)
                : baseScale;
            AvatarScaleTransform.ScaleX = effectiveScale;
            AvatarScaleTransform.ScaleY = effectiveScale;
        }

        if (pos == "AutoEdge" || pos == "Free" || string.IsNullOrWhiteSpace(pos))
        {
            // When Right split-screen dock is open, dock to BottomLeft so the right split pane isn't obstructed;
            // otherwise dock to BottomRight.
            string resolvedDock = isRightSplitOpen ? "BottomLeft" : "BottomRight";
            ApplyDockAlignment(resolvedDock);
            if (announce)
            {
                Say(isRightSplitOpen
                    ? "🤖 偵測到右側分屏開啟，已自動切換停靠至左下角！"
                    : "🤖 智慧貼邊模式：已自動停靠於視窗右側邊緣！", 2800);
            }
        }
        else
        {
            ApplyDockAlignment(pos);
        }
    }

    private void ApplyDockAlignment(string dock)
    {
        AvatarPositionTransform.X = 0;
        AvatarPositionTransform.Y = 0;

        switch (dock)
        {
            case "BottomLeft":
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Bottom;
                Margin = new Thickness(20, 0, 0, 36);
                break;
            case "TopRight":
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Top;
                Margin = new Thickness(0, 48, 20, 0);
                break;
            case "TopLeft":
                HorizontalAlignment = HorizontalAlignment.Left;
                VerticalAlignment = VerticalAlignment.Top;
                Margin = new Thickness(20, 48, 0, 0);
                break;
            case "BottomRight":
            default:
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Bottom;
                Margin = new Thickness(0, 0, 20, 36);
                break;
        }
    }

    private void UserControl_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
        this.ReleasePointerCapture(e.Pointer);
    }

    private void UserControl_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void HealthBadge_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Say($"當前磁碟指標: {HealthBadgeText.Text}，運作溫度 {HealthBadgeTemp.Text}。S.M.A.R.T. 定期監控中～", 4000);
        e.Handled = true;
    }

    private void PlayRandomQuote()
    {
        var text = HealthBadgeText.Text;
        if (text.Contains("異常") || text.Contains("危險"))
        {
            Say(_alertQuotes[_rand.Next(_alertQuotes.Length)], 4500);
        }
        else if (text.Contains("注意"))
        {
            Say(_cautionQuotes[_rand.Next(_cautionQuotes.Length)], 4500);
        }
        else
        {
            Say(_healthyQuotes[_rand.Next(_healthyQuotes.Length)], 4000);
        }
        TriggerBlink();
    }

    // ── Dock Positioning ──
    public void SetDockPosition(string dock, bool save = true)
    {
        if (dock == "AutoEdge")
        {
            if (save)
            {
                var s = SettingsService.Instance.Current;
                s.CompanionPosition = "AutoEdge";
                s.CompanionOffsetX = 0;
                s.CompanionOffsetY = 0;
                SettingsService.Instance.Save();
            }
            bool rightSplitOpen = MainWindow.CurrentInstance?.IsRightDockOpen ?? false;
            AutoDetectAndSnapToEdge(rightSplitOpen, announce: save);
            return;
        }

        ApplyDockAlignment(dock);

        if (save)
        {
            var s = SettingsService.Instance.Current;
            s.CompanionPosition = dock;
            s.CompanionOffsetX = 0;
            s.CompanionOffsetY = 0;
            SettingsService.Instance.Save();
        }
    }

    // ── Scaling ──
    public void SetScale(double factor, bool save = true)
    {
        AvatarScaleTransform.ScaleX = factor;
        AvatarScaleTransform.ScaleY = factor;

        if (save)
        {
            var s = SettingsService.Instance.Current;
            s.CompanionScale = factor;
            SettingsService.Instance.Save();
        }
    }

    // ── Context Menu Actions ──
    private void Dock_AutoEdge_Click(object sender, RoutedEventArgs e) => SetDockPosition("AutoEdge");
    private void Dock_BottomRight_Click(object sender, RoutedEventArgs e) => SetDockPosition("BottomRight");
    private void Dock_BottomLeft_Click(object sender, RoutedEventArgs e) => SetDockPosition("BottomLeft");
    private void Dock_TopRight_Click(object sender, RoutedEventArgs e) => SetDockPosition("TopRight");
    private void Dock_TopLeft_Click(object sender, RoutedEventArgs e) => SetDockPosition("TopLeft");
    private void ResetPosition_Click(object sender, RoutedEventArgs e) => SetDockPosition("AutoEdge");

    private void Size_Mini_Click(object sender, RoutedEventArgs e) => SetScale(0.70);
    private void Size_Normal_Click(object sender, RoutedEventArgs e) => SetScale(1.0);
    private void Size_Large_Click(object sender, RoutedEventArgs e) => SetScale(1.25);

    private void Emotion_Auto_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.CompanionEmotionState = "Auto";
        SettingsService.Instance.Save();
        Say("已切換為自動感應磁碟健康表情模式～", 3000);
    }

    private void Emotion_Happy_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.CompanionEmotionState = "Happy";
        SettingsService.Instance.Save();
        SetEmotion(CompanionEmotion.Happy);
    }

    private void Emotion_Caution_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.CompanionEmotionState = "Caution";
        SettingsService.Instance.Save();
        SetEmotion(CompanionEmotion.Caution);
    }

    private void Emotion_Alert_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.CompanionEmotionState = "Alert";
        SettingsService.Instance.Save();
        SetEmotion(CompanionEmotion.Alert);
    }

    private void Emotion_Working_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.CompanionEmotionState = "Working";
        SettingsService.Instance.Save();
        SetEmotion(CompanionEmotion.Working);
    }

    private void ToggleBadge_Click(object sender, RoutedEventArgs e)
    {
        var show = MenuToggleBadge.IsChecked;
        HealthBadgeBorder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SettingsService.Instance.Current.CompanionShowBadge = show;
        SettingsService.Instance.Save();
    }

    private void ToggleBubble_Click(object sender, RoutedEventArgs e)
    {
        var show = MenuToggleBubble.IsChecked;
        SpeechBubble.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleGaze_Click(object sender, RoutedEventArgs e)
    {
        var gaze = MenuToggleGaze.IsChecked;
        SettingsService.Instance.Current.CompanionEnableGazeTracking = gaze;
        SettingsService.Instance.Save();
    }

    private void HideCompanion_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.Current.EnableCompanionAvatar = false;
        SettingsService.Instance.Save();
        this.Visibility = Visibility.Collapsed;
    }

    // ── Custom Model / Image Loading ──
    public void LoadCustomModel(string modelPath)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
            {
                var ext = Path.GetExtension(modelPath).ToLowerInvariant();
                if (ext is ".png" or ".webp" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
                {
                    try
                    {
                        CustomAvatarImage.Source = new BitmapImage(new Uri(modelPath));
                        CustomAvatarImage.Visibility = Visibility.Visible;
                        VectorAvatarCanvas.Visibility = Visibility.Collapsed;
                        return;
                    }
                    catch { }
                }
            }

            CustomAvatarImage.Visibility = Visibility.Collapsed;
            VectorAvatarCanvas.Visibility = Visibility.Visible;
        });
    }
}
