using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;
using DiskMasterWinUI.ViewModels;

namespace DiskMasterWinUI.Pages;

public sealed partial class SystemOptimizerPage : Page
{
    public SystemOptimizerViewModel ViewModel { get; } = new();
    private bool _suppressToggled = false;

    public SystemOptimizerPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        DiskMasterWinUI.Helpers.TabReorderHelper.Attach(OptimizerTabView);
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;

        RefreshAllBtnText.Text = lang switch { "zh-CN" => "刷新系统状态", "en-US" => "Refresh System Status", "ja-JP" => "システム状態を更新", _ => "重新整理系統狀態" };

        TabGaming.Header = lang switch { "zh-CN" => "🎮 电竞游戏模式", "en-US" => "🎮 Gaming Mode", "ja-JP" => "🎮 ゲーミングモード", _ => "🎮 電競遊戲模式" };
        TabCpu.Header = lang switch { "zh-CN" => "⚡ CPU与调度优先级", "en-US" => "⚡ CPU & Scheduling", "ja-JP" => "⚡ CPU とスケジューリング", _ => "⚡ CPU與排程優先權" };
        TabPower.Header = lang switch { "zh-CN" => "🔋 电源计划与核心唤醒", "en-US" => "🔋 Power Plans & Core Unparking", "ja-JP" => "🔋 電源プランとコアアンパーキング", _ => "🔋 電源計畫與核心喚醒" };
        TabBackup.Header = lang switch { "zh-CN" => "💾 注册表备份与还原", "en-US" => "💾 Registry Backup & Restore", "ja-JP" => "💾 レジストリバックアップと復元", _ => "💾 登錄檔備份與還原" };
        TabOneDrive.Header = lang switch { "zh-CN" => "☁️ OneDrive 深度治理与卸载", "en-US" => "☁️ OneDrive Governance & Uninstall", "ja-JP" => "☁️ OneDrive 管理と削除", _ => "☁️ OneDrive 深度治理與卸載" };
        TabDrivers.Header = lang switch { "zh-CN" => "🏎️ OEM 驱动清理 (PnPUtil)", "en-US" => "🏎️ OEM Driver Cleanup (PnPUtil)", "ja-JP" => "🏎️ OEM ドライバの削除 (PnPUtil)", _ => "🏎️ OEM 驅動清理 (PnPUtil)" };

        HeroTitle.Text = lang switch { "zh-CN" => "电竞极致模式", "en-US" => "Ultra Gaming Mode (1-Click)", "ja-JP" => "ウルトラゲーミングモード", _ => "電競遊戲極致模式" };
        HeroSubtitle.Text = lang switch
        {
            "zh-CN" => "一键同步调校：CPU 量子时间分配 (0x26)、启用终极效能电源计划、解锁 100% 前台算力 (SystemResponsiveness=0)、关闭网络节流防 Ping 突增、关闭 Game DVR 背景录制、开启 HAGS 硬件加速与 1:1 传感器零延迟。",
            "en-US" => "1-Click optimization: CPU Quantum Scheduling (0x26), Ultimate Performance Power Plan, 100% Foreground CPU (SystemResponsiveness=0), Network Throttling Disabled, Game DVR Disabled, HAGS enabled, and 1:1 input raw tracking.",
            "ja-JP" => "ワンクリック最適化：CPU量子スケジューリング (0x26)、究極パフォーマンス電源プラン、フォアグラウンドCPU 100%、ネットワーク絞りを無効化、Game DVR無効、HAGS有効、1:1センサー追跡。",
            _ => "一鍵同步調校：CPU 量子時間分配 (0x26)、啟用終極效能電源計畫、解鎖 100% 前台算力 (SystemResponsiveness=0)、關閉網路節流防 Ping 突增、關閉 Game DVR 背景錄影負擔、開啟 HAGS 硬體加速與 1:1 感應器零延遲。"
        };

        ApplyGamingBtn.Content = lang switch { "zh-CN" => "🎮 一键应用电竞模式", "en-US" => "🎮 Apply Ultra Gaming Mode", "ja-JP" => "🎮 ゲーミングモードを適用", _ => "🎮 一鍵套用電競模式" };
        RestoreDefaultsBtn.Content = lang switch { "zh-CN" => "🛡️ 还原默认设置", "en-US" => "🛡️ Restore Factory Defaults", "ja-JP" => "🛡️ 工場デフォルトに戻す", _ => "🛡️ 還原官方預設" };

        // Tab 1: Gaming
        GpuCardTitle.Text = lang switch { "zh-CN" => "背景录制与显卡硬件调度", "en-US" => "Background Recording & GPU Scheduling", "ja-JP" => "バックグラウンド録画とGPUスケジューリング", _ => "背景錄影與顯卡硬體排程" };
        GameDvrTitle.Text = lang switch { "zh-CN" => "禁用 Game DVR / Xbox Game Bar", "en-US" => "Disable Game DVR / Xbox Game Bar", "ja-JP" => "Game DVR / Xbox Game Bar を無効化", _ => "停用 Game DVR / Xbox Game Bar" };
        GameDvrDesc.Text = lang switch { "zh-CN" => "彻底关闭背景录制与快照虚拟层，释放显卡与 CPU 资源。", "en-US" => "Completely disable background recording to free up GPU and CPU resources.", "ja-JP" => "バックグラウンド録画を停止し、GPUとCPUリソースを解放します。", _ => "徹底關閉背景錄影與快照虛擬層，釋放顯卡與 CPU 資源。" };
        HagsTitle.Text = lang switch { "zh-CN" => "硬件加速 GPU 调度 (HAGS)", "en-US" => "Hardware-Accelerated GPU Scheduling (HAGS)", "ja-JP" => "ハードウェア アクセラレータによる GPU スケジューリング (HAGS)", _ => "硬體加速 GPU 排程 (HAGS)" };
        HagsDesc.Text = lang switch { "zh-CN" => "由 GPU 专属芯片直接管理显存，减少帧生成延迟。(需显卡支持并重启)", "en-US" => "Allows GPU to manage VRAM directly to reduce frame latency. (Requires GPU support & reboot)", "ja-JP" => "GPUが直接VRAMを管理し、フレーム遅延を削減します（要再起動）。", _ => "由 GPU 專屬晶片直接管理 VRAM，減少幀生成延遲。(需顯卡支援並重開機 🟡)" };

        InputCardTitle.Text = lang switch { "zh-CN" => "电竞外设低延迟响应", "en-US" => "Ultra-Low Input Latency Response", "ja-JP" => "超低入力レイテンシ応答", _ => "電競周邊零延遲響應" };
        InputLatencyTitle.Text = lang switch { "zh-CN" => "鼠标 1:1 传感器追踪与键盘低延迟", "en-US" => "Mouse 1:1 Sensor Tracking & Zero Delay Keyboard", "ja-JP" => "マウス1:1センサートラッキング＆低遅延キーボード", _ => "滑鼠 1:1 感應器追蹤 & 鍵盤零延遲" };
        InputLatencyDesc.Text = lang switch { "zh-CN" => "移除指针平滑加速曲线，将键盘重复延迟设为最低，消除动画等待时间。", "en-US" => "Removes pointer acceleration, sets keyboard repeat delay to minimum, eliminates menu delays.", "ja-JP" => "ポインタ加速を無効化し、キーボード応答を最速化し、UIアニメーション遅延を削減します。", _ => "移除指針平滑加速度曲線，將鍵盤重複延遲設為 0，消除選單動畫等待時間。" };

        // Tab 2: CPU & MMCSS & MMAgent
        CpuPriorityTitle.Text = lang switch { "zh-CN" => "CPU 量子时间分配 (Win32PrioritySeparation)", "en-US" => "CPU Quantum Scheduling (Win32PrioritySeparation)", "ja-JP" => "CPU クォンタム時間割り当て (Win32PrioritySeparation)", _ => "CPU 量子時間分配 (Win32PrioritySeparation)" };
        ApplyCpuPresetBtn.Content = lang switch { "zh-CN" => "⚡ 应用 CPU 调度方案", "en-US" => "⚡ Apply CPU Preset", "ja-JP" => "⚡ CPU スケジュールを適用", _ => "⚡ 套用 CPU 排程方案" };
        CpuPresetCombo.Header = lang switch { "zh-CN" => "选择调度方案", "en-US" => "Select Scheduling Preset", "ja-JP" => "スケジューリングプリセットを選択", _ => "選擇排程方案 (FPSHeaven & Windows Internals 標準)" };
        CpuComboItem0.Content = lang switch { "zh-CN" => "0x26 (Dec 38): 电竞通用推荐 (短量子 + 前景 3 倍优先级，流畅响应)", "en-US" => "0x26 (Dec 38): Gaming Recommended (Short Quantum + 3x Foreground Boost)", "ja-JP" => "0x26 (Dec 38): ゲーミング推奨 (短いクォンタム + フォアグラウンド3倍ブースト)", _ => "0x26 (Dec 38): 電競通用推薦 (短量子 + 前景 3 倍優先權，流暢響應)" };
        CpuComboItem1.Content = lang switch { "zh-CN" => "0x2A (Dec 42): FPS 极致竞技 (短量子 + 固定配额 + 前景 3 倍，最低延迟)", "en-US" => "0x2A (Dec 42): FPS Esports (Short + Fixed + 3x Foreground Boost, Lowest Latency)", "ja-JP" => "0x2A (Dec 42): FPS 競技用 (短い + 固定クォンタム + 3倍ブースト、最小遅延)", _ => "0x2A (Dec 42): FPSHeaven 極致競技 (短量子 + 固定配額 + 前景 3 倍，杜絕排程抖動 Jitter，輸入延遲最低)" };
        CpuComboItem2.Content = lang switch { "zh-CN" => "0x28 (Dec 40): 消除微卡顿 (短量子 + 固定配额 + 平权，稳定 1% Low)", "en-US" => "0x28 (Dec 40): Smooth Frametime (Short + Fixed + Equal, Optimal 1% Low FPS)", "ja-JP" => "0x28 (Dec 40): マイクロスタッター解消 (短い + 固定クォンタム + 均等、安定した1% Low)", _ => "0x28 (Dec 40): FPSHeaven 消除微卡頓 (短量子 + 固定配額 + 平權，使 1% Low 幀率極致穩定)" };
        CpuComboItem3.Content = lang switch { "zh-CN" => "0x16 (Dec 22): 经典 FPS 模式 (长量子 + 前景 3 倍)", "en-US" => "0x16 (Dec 22): Classic FPS Mode (Long Quantum + 3x Foreground Boost)", "ja-JP" => "0x16 (Dec 22): クラシックFPS (長いクォンタム + 3倍ブースト)", _ => "0x16 (Dec 22): 經典 FPS 模式 (長量子 + 前景 3 倍)" };
        CpuComboItem4.Content = lang switch { "zh-CN" => "0x18 (Dec 24): 高性能工作站/服务器 (长量子 + 固定配额，高吞吐量)", "en-US" => "0x18 (Dec 24): High Throughput Workstation (Long Quantum + Fixed)", "ja-JP" => "0x18 (Dec 24): ワークステーション (長いクォンタム + 固定、高スループット)", _ => "0x18 (Dec 24): 高效能工作站/伺服器 (長量子 + 固定配額，高吞吐量)" };
        CpuComboItem5.Content = lang switch { "zh-CN" => "0x02 (Dec 2): Windows 官方默认值 (Default)", "en-US" => "0x02 (Dec 2): Windows Default", "ja-JP" => "0x02 (Dec 2): Windows デフォルト", _ => "0x02 (Dec 2): Windows 官方預設值 (Default)" };

        CpuExplanationInfoBar.Title = lang switch { "zh-CN" => "调度架构原理", "en-US" => "Scheduling Architecture Principle", "ja-JP" => "スケジューリングアーキテクチャの原理", _ => "排程架構原理" };
        CpuExplanationInfoBar.Message = lang switch
        {
            "zh-CN" => "固定时间配额 (Fixed Quantum) 能防止背景任务突发打断游戏线程，在 CS2、Valorant 等电竞游戏中能大幅改善操作一致性与 1% Low 帧率。修改后重启生效。",
            "en-US" => "Fixed Quantum prevents background tasks from interrupting game threads, significantly improving 1% Low framerates and aim consistency in competitive titles. Requires reboot.",
            "ja-JP" => "固定クォンタム (Fixed Quantum) により、バックグラウンドタスクがゲームスレッドを中断するのを防ぎ、1% Low フレームレートと操作の安定性を劇的に改善します（要再起動）。",
            _ => "固定時間配額 (Fixed Quantum) 能防止背景任務突發打斷遊戲執行緒，在 CS2、Valorant 等電競遊戲中能大幅改善準心操作一致性與 1% Low 幀率。修改後重開機生效。"
        };

        MmcssCardTitle.Text = lang switch { "zh-CN" => "多媒体类调度器 (MMCSS) 与能耗节流", "en-US" => "Multimedia Class Scheduler (MMCSS) & Power Throttling", "ja-JP" => "マルチメディアクラススケジューラ (MMCSS) と電力スロットリング", _ => "多媒體類別排程器 (MMCSS) 與能源節流" };
        PowerThrottlingLabel.Text = lang switch { "zh-CN" => "禁用 CPU 能耗节流 (PowerThrottlingOff = 1)", "en-US" => "Disable CPU Power Throttling (PowerThrottlingOff = 1)", "ja-JP" => "CPU 電力スロットリングを無効化 (PowerThrottlingOff = 1)", _ => "停用 CPU 能源節流 (PowerThrottlingOff = 1)" };
        PowerThrottlingDesc.Text = lang switch { "zh-CN" => "防止 Windows 10/11 内核对背景线程或非活动游戏窗口强制降频节能。", "en-US" => "Prevents Windows kernel from aggressively throttling background threads or inactive game windows.", "ja-JP" => "Windows カーネルがバックグラウンドスレッドやゲームウィンドウの周波数を低下させるのを防ぎます。", _ => "防止 Windows 10/11 核心對背景執行緒或非活動遊戲視窗強制降頻節能。" };

        SystemResponsivenessLabel.Text = lang switch { "zh-CN" => "100% 前台 CPU 算力分配 (SystemResponsiveness = 0)", "en-US" => "100% Foreground CPU Priority (SystemResponsiveness = 0)", "ja-JP" => "100% フォアグラウンド CPU 優先割り当て (SystemResponsiveness = 0)", _ => "100% 前台 CPU 算力分配 (SystemResponsiveness = 0)" };
        SystemResponsivenessDesc.Text = lang switch { "zh-CN" => "释放 Windows 原生预留给系统背景服务的 20% CPU 保留份额，全数交由前台游戏享用。", "en-US" => "Unlocks the 20% CPU reservation normally held by background services, dedicating 100% to games.", "ja-JP" => "バックグラウンドサービス用に予約されている20%のCPU割り当てを解放し、全力をフォアグラウンドに集中させます。", _ => "釋放 Windows 原生預留給系統背景服務的 20% CPU 保留份額，全數交由前台遊戲享用。" };

        NetworkThrottlingLabel.Text = lang switch { "zh-CN" => "禁用网络数据包限速队列 (NetworkThrottlingIndex = 0xFFFFFFFF)", "en-US" => "Disable Network Packet Throttling (NetworkThrottlingIndex = 0xFFFFFFFF)", "ja-JP" => "ネットワークパケットスロットリングを無効化 (NetworkThrottlingIndex = 0xFFFFFFFF)", _ => "停用網路封包限速佇列 (NetworkThrottlingIndex = 0xFFFFFFFF)" };
        NetworkThrottlingDesc.Text = lang switch { "zh-CN" => "禁用 Windows 在游戏与音频执行期间的网络限速队列，彻底解决网游 Ping 波动与丢包。", "en-US" => "Disables network packet throttling during gaming and multimedia, preventing ping spikes and packet loss.", "ja-JP" => "ゲームやメディア再生中のネットワーク絞り込みを停止し、Pingの乱高下やパケットロスを防ぎます。", _ => "停用 Windows 在遊戲與音訊執行期間的網路限速佇列，徹底解決線上遊戲 Ping 忽高忽低與丟包問題。" };

        MmAgentCardTitle.Text = lang switch { "zh-CN" => "内存管理代理 (MMAgent)", "en-US" => "Memory Management Agent (MMAgent)", "ja-JP" => "メモリ管理エージェント (MMAgent)", _ => "🧠 記憶體管理代理 (MMAgent - Memory Management)" };
        QuickGamingRamBtn.Content = lang switch { "zh-CN" => "🎮 一键电竞低延迟 (0压缩/0合并)", "en-US" => "🎮 1-Click Gaming RAM (No Compression)", "ja-JP" => "🎮 ゲーミング低遅延 (圧縮・結合オフ)", _ => "🎮 一鍵電競低延遲 (0壓縮/0合併)" };
        RestoreStockRamBtn.Content = lang switch { "zh-CN" => "🛡️ 还原默认设置", "en-US" => "🛡️ Restore Factory Defaults", "ja-JP" => "🛡️ 工場デフォルトに戻す", _ => "🛡️ 還原官方預設" };

        MemCompressionLabel.Text = lang switch { "zh-CN" => "内存压缩 (Memory Compression)", "en-US" => "Memory Compression", "ja-JP" => "メモリ圧縮 (Memory Compression)", _ => "記憶體壓縮 (Memory Compression -mc)" };
        MemCompressionDesc.Text = lang switch { "zh-CN" => "关闭可杜绝高负载游戏期间 CPU 背景解压缩分页引发的突发 CPU 尖峰与微卡顿。", "en-US" => "Disabling eliminates CPU spikes caused by background page decompression during gaming.", "ja-JP" => "無効にすると、ゲームプレイ中のバックグラウンド展開によるCPUスパイクとスタッターを防止します。", _ => "關閉可杜絕高負載遊戲期間 CPU 背景解壓縮分頁引發的突發 CPU 尖峰與微卡頓 (建議 16GB/32GB+ RAM 遊戲機關閉)。" };

        PageCombiningLabel.Text = lang switch { "zh-CN" => "内存分页合并 (Page Combining)", "en-US" => "Page Combining", "ja-JP" => "ページ結合 (Page Combining)", _ => "記憶體分頁合併 (Page Combining)" };
        PageCombiningDesc.Text = lang switch { "zh-CN" => "关闭可防止 Windows 定期于背景扫描并合并重复内存分页，大幅降低游戏期间之内存访问延迟。", "en-US" => "Disabling prevents periodic scanning and combining of duplicate pages, reducing RAM latency.", "ja-JP" => "無効化により、バックグラウンドでの重複ページ検索を停止し、RAMアクセス遅延を最小限に抑えます。", _ => "關閉可防止 Windows 定期於背景掃描並合併重複記憶體分頁，大幅降低遊戲期間之記憶體存取延遲。" };

        AppPreLaunchLabel.Text = lang switch { "zh-CN" => "应用程序预先启动 (Application PreLaunch)", "en-US" => "Application PreLaunch", "ja-JP" => "アプリケーションプリ起動 (Application PreLaunch)", _ => "應用程式預先啟動 (Application PreLaunch)" };
        AppPreLaunchDesc.Text = lang switch { "zh-CN" => "控制 Windows 是否在背景预先唤醒并载入常用 UWP / 系统应用程序。", "en-US" => "Controls whether Windows preloads frequent UWP apps in the background.", "ja-JP" => "WindowsがバックグラウンドでUWPアプリを事前にメモリへ読み込むかを制御します。", _ => "控制 Windows 是否在背景預先喚醒並載入常用 UWP / 系統應用程式。" };

        AppLaunchProfilingLabel.Text = lang switch { "zh-CN" => "启动分析评测 (Application Launch Profiling)", "en-US" => "Application Launch Profiling", "ja-JP" => "起動プロファイリング (Application Launch Profiling)", _ => "啟動分析評測 (Application Launch Profiling)" };
        AppLaunchProfilingDesc.Text = lang switch { "zh-CN" => "控制 Superfetch / SysMain 服务对应用程序启动时序之长期监控与行为分析。", "en-US" => "Controls SysMain monitoring and profiling of app launch timelines.", "ja-JP" => "SysMain サービスによるアプリ起動パフォーマンスの継続的監視を制御します。", _ => "控制 Superfetch / SysMain 服務對應用程式啟動時序之長期監控與行為分析。" };

        OpRecordingLabel.Text = lang switch { "zh-CN" => "系统操作记录 (Operation Recording)", "en-US" => "Operation Recording", "ja-JP" => "操作記録トレース (Operation Recording)", _ => "系統操作記錄 (Operation Recording)" };
        OpRecordingDesc.Text = lang switch { "zh-CN" => "控制 Prefetch 追踪开机与操作磁盘读取模式之记录机制。", "en-US" => "Controls Prefetch tracing of boot and disk read operations.", "ja-JP" => "Prefetch による起動時およびディスク読み取りパターンのトレース記録を制御します。", _ => "控制 Prefetch 追蹤開機與操作磁碟讀取模式之記錄機制。" };

        // Tab 3: Power Plans & Wake
        PowerQuickTitle.Text = lang switch { "zh-CN" => "电源配置快速调校与旗舰解锁", "en-US" => "Power Scheme Quick Tuning & Unlocking", "ja-JP" => "電源スキームのクイック設定とロック解除", _ => "電源配置快速調校與旗艦解鎖" };
        UnlockUltimateBtn.Content = lang switch { "zh-CN" => "⚡ 解锁并启用「卓越性能」", "en-US" => "⚡ Unlock & Enable Ultimate Performance", "ja-JP" => "⚡ 「究極のパフォーマンス」を有効化", _ => "⚡ 解鎖並啟用「終極效能 (Ultimate Performance)」" };
        UnlockHighBtn.Content = lang switch { "zh-CN" => "🚀 解锁「高性能」", "en-US" => "🚀 Unlock High Performance", "ja-JP" => "🚀 「高パフォーマンス」を解放", _ => "🚀 解鎖「高效能 (High Performance)」" };

        CoreUnparkingLabel.Text = lang switch { "zh-CN" => "CPU 核心防休眠 (Core Unparking)", "en-US" => "Disable CPU Core Parking (Core Unparking)", "ja-JP" => "CPU コアアンパーキング (Core Unparking)", _ => "CPU 核心防休眠 (Core Unparking)" };
        CoreUnparkingDesc.Text = lang switch { "zh-CN" => "设置 CPMINCORES=100，让所有核心随时处于启用状态，消除动态唤醒微卡顿。", "en-US" => "Sets CPMINCORES=100 so all cores remain active, preventing wake latency stutters.", "ja-JP" => "CPMINCORES=100 に設定し、全コアを常時稼働させてコア復帰時の微細なカクつきを防ぎます。", _ => "設定 CPMINCORES=100，讓所有核心隨時處於啟用狀態，消除動態喚醒微卡頓。" };

        MinProcessorStateLabel.Text = lang switch { "zh-CN" => "锁定最低 CPU 状态 100%", "en-US" => "Lock Minimum CPU State to 100%", "ja-JP" => "最小プロセッサ状態を100%に固定", _ => "鎖定最低 CPU 狀態 100%" };
        MinProcessorStateDesc.Text = lang switch { "zh-CN" => "设置 PROCTHROTTLEMIN=100，插电时杜绝降频，确保游戏转场时刻维持极速。", "en-US" => "Sets PROCTHROTTLEMIN=100 to prevent clock downthrottling, maintaining peak speeds.", "ja-JP" => "PROCTHROTTLEMIN=100 に設定し、周波数低下を防ぎ、常にフルスピードを維持します。", _ => "設定 PROCTHROTTLEMIN=100，插電時杜絕降頻，確保遊戲轉場時刻維持極速。" };

        InstalledPlansTitle.Text = lang switch { "zh-CN" => "现有电源计划清单", "en-US" => "Installed Power Schemes", "ja-JP" => "インストール済み電源プラン", _ => "現存電源計畫清單 (Installed Schemes)" };

        HibernateCardTitle.Text = lang switch { "zh-CN" => "⚡ 休眠管理与 SSD 空间瞬间释放", "en-US" => "⚡ Hibernation Management & SSD Space Reclamation", "ja-JP" => "⚡ 休止状態の管理と SSD 空き領域の即時解放", _ => "⚡ 休眠管理與 SSD 空間瞬間釋放 (PowerCfg Hibernate)" };
        HibernateLabel.Text = lang switch { "zh-CN" => "Windows 休眠功能 (Hibernate)", "en-US" => "Windows Hibernation", "ja-JP" => "Windows 休止状態 (Hibernate)", _ => "Windows 休眠功能 (Hibernate - powercfg -h off/on)" };
        HibernateDesc.Text = lang switch { "zh-CN" => "关闭休眠将立即删除 C:\\hiberfil.sys，直接为系统磁盘释放 16GB ~ 64GB 庞大可用空间！", "en-US" => "Disabling instantly deletes C:\\hiberfil.sys, reclaiming 16GB - 64GB of drive space!", "ja-JP" => "休止状態を無効化すると C:\\hiberfil.sys が即座に削除され、16GB〜64GB の容量を解放します。", _ => "關閉休眠將立即刪除 C:\\hiberfil.sys，直接為系統磁碟釋放 16GB ~ 64GB 龐大可用空間！" };

        FastStartupReducedLabel.Text = lang switch { "zh-CN" => "快速启动缩减模式 (Fast Startup Reduced)", "en-US" => "Fast Startup Reduced Mode", "ja-JP" => "高速スタートアップ縮小モード", _ => "快速開機縮減模式 (Fast Startup Reduced Mode)" };
        FastStartupReducedDesc.Text = lang switch { "zh-CN" => "使用 /type reduced 模式，保留 Windows 快速启动优点，同时休眠档容量大幅减少 60%。", "en-US" => "Uses /type reduced mode to keep fast startup while shrinking the hiberfil size by 60%.", "ja-JP" => "/type reduced を適用し、高速スタートアップを維持しつつ休止ファイルのサイズを60%削減します。", _ => "使用 /type reduced 模式，保留 Windows 快速開機優點，同時休眠檔容量大幅減少 60%。" };

        ProcessorBoostLabel.Text = lang switch { "zh-CN" => "解锁处理器性能激进提升原则", "en-US" => "Unlock Processor Performance Boost Mode", "ja-JP" => "プロセッサ パフォーマンス向上モードを解放", _ => "解鎖處理器效能激進提升原則 (Processor Performance Boost Mode)" };
        ProcessorBoostDesc.Text = lang switch { "zh-CN" => "解锁 Windows 隐藏的 CPU 激进睿频策略，可在电源选项高级设置中自由选择激进/禁用/高性能提升。", "en-US" => "Reveals hidden CPU boost policies in Windows power options to select Aggressive/Disabled/Efficient.", "ja-JP" => "電源オプションの詳細設定にある隠されたCPUブーストポリシーを解放します。", _ => "解鎖 Windows 隱藏的 CPU 激進睿頻策略，可在電源選項進階設定中自由選擇激進/停用/高效能提升。" };

        WakeDevicesTitle.Text = lang switch { "zh-CN" => "🔌 硬件睡眠唤醒管理器", "en-US" => "🔌 Wake-Armed Hardware Devices", "ja-JP" => "🔌 ハードウェア スリープ復帰管理", _ => "🔌 硬體睡眠喚醒管理器 (Wake-Armed Devices)" };
        WakeDevicesDesc.Text = lang switch { "zh-CN" => "杜绝半夜鼠标晃动、键盘误碰或网络唤醒包唤醒电脑。每台硬件均设有独立开关：", "en-US" => "Prevent accidental wakeups from mouse bumps or network packets. Per-device toggles:", "ja-JP" => "マウスの振動やネットワークパケットによる深夜の誤復帰を防止。デバイスごとの個別切り替え：", _ => "杜絕半夜滑鼠晃動、鍵盤誤碰或網路魔術封包喚醒電腦。每台硬體均設有獨立開關：" };
        RefreshWakeDevicesBtn.Content = lang switch { "zh-CN" => "🔄 刷新唤醒清单", "en-US" => "🔄 Refresh Wake Devices", "ja-JP" => "🔄 復帰デバイスを更新", _ => "🔄 重新整理喚醒清單" };

        SleepBlockerTitle.Text = lang switch { "zh-CN" => "🌙 睡眠阻挡诊断器与能耗健康报告", "en-US" => "🌙 Sleep Blocker Diagnostics & Energy Reports", "ja-JP" => "🌙 スリープ阻害要因の診断とバッテリー診断", _ => "🌙 睡眠阻擋診斷器與能耗健康報告" };
        SleepBlockerDesc.Text = lang switch { "zh-CN" => "检测阻挡电脑休眠的背景程序 (powercfg /requests) 或生成专业电池健康报告。", "en-US" => "Diagnoses background processes preventing sleep (powercfg /requests) or generates battery reports.", "ja-JP" => "スリープを阻害するプロセス（powercfg /requests）の診断や詳細なバッテリー診断を実行します。", _ => "檢測阻擋電腦休眠的背景程序 (powercfg /requests) 或產出專業電池健康報告。" };
        RefreshSleepBlockersBtn.Content = lang switch { "zh-CN" => "🔍 诊断阻挡程序", "en-US" => "🔍 Diagnose Blockers", "ja-JP" => "🔍 阻害プロセスを診断", _ => "🔍 診斷阻擋程序" };
        BatteryReportBtn.Content = lang switch { "zh-CN" => "🔋 生成电池健康报告", "en-US" => "🔋 Battery Report", "ja-JP" => "🔋 バッテリーレポート", _ => "🔋 產生電池健康報告" };
        EnergyReportBtn.Content = lang switch { "zh-CN" => "⚡ 生成能耗诊断报告", "en-US" => "⚡ Energy Report", "ja-JP" => "⚡ エネルギー診断", _ => "⚡ 產生能耗診斷報告" };

        // Tab 4: Backup & Restore
        BackupSectionTitle.Text = lang switch { "zh-CN" => "建立备份与系统还原点", "en-US" => "Create Registry Backup & System Restore Point", "ja-JP" => "レジストリバックアップと復元ポイントの作成", _ => "建立備份與系統還原點" };
        BackupDescBox.Header = lang switch { "zh-CN" => "备份说明", "en-US" => "Backup Description", "ja-JP" => "バックアップの説明", _ => "備份說明 (Description)" };
        BackupDescBox.PlaceholderText = lang switch { "zh-CN" => "例如: 游戏前优化备份", "en-US" => "e.g., Pre-optimization backup", "ja-JP" => "例: 最適化前のバックアップ", _ => "例如: 遊戲前優化備份" };
        CreateManualBackupBtn.Content = lang switch { "zh-CN" => "💾 创建注册表备份", "en-US" => "💾 Create Registry Backup", "ja-JP" => "💾 レジストリをバックアップ", _ => "💾 建立登錄檔備份" };
        CreateRestorePointBtn.Content = lang switch { "zh-CN" => "🛡️ 创建系统还原点", "en-US" => "🛡️ Create Restore Point", "ja-JP" => "🛡️ 復元ポイントを作成", _ => "🛡️ 建立系統還原點" };
        OpenBackupFolderBtn.Content = lang switch { "zh-CN" => "📂 打开备份文件夹", "en-US" => "📂 Open Backup Folder", "ja-JP" => "📂 保存先フォルダを開く", _ => "📂 開啟備份資料夾" };

        BackupsListTitle.Text = lang switch { "zh-CN" => "已创建的注册表快照与备份 (.reg)", "en-US" => "Registry Snapshots & Backups (.reg)", "ja-JP" => "作成されたレジストリスナップショット (.reg)", _ => "已建立的登錄檔快照與備份 (.reg)" };
        RestorePointsListTitle.Text = lang switch { "zh-CN" => "Windows 系统还原点清单", "en-US" => "Windows System Restore Points", "ja-JP" => "Windows システム復元ポイント一覧", _ => "Windows 系統還原點清單 (System Restore Points)" };

        ConsoleLogTitle.Text = lang switch { "zh-CN" => "系统最优化终端记录", "en-US" => "System Optimizer Console Log", "ja-JP" => "システム最適化コンソールログ", _ => "系統最佳化與排程調校終端記錄" };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressToggled = true;
        try
        {
            ApplyLanguage();
            await ViewModel.RefreshAllCommand.ExecuteAsync(null);
        }
        finally
        {
            _suppressToggled = false;
        }
    }

    private void GameDvr_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleGameDvrCommand.CanExecute(null))
            ViewModel.ToggleGameDvrCommand.Execute(null);
    }

    private void Hags_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleHagsCommand.CanExecute(null))
            ViewModel.ToggleHagsCommand.Execute(null);
    }

    private void InputLatency_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleInputLatencyCommand.CanExecute(null))
            ViewModel.ToggleInputLatencyCommand.Execute(null);
    }

    private void PowerThrottling_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.TogglePowerThrottlingCommand.CanExecute(null))
            ViewModel.TogglePowerThrottlingCommand.Execute(null);
    }

    private void SystemResponsiveness_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleSystemResponsivenessCommand.CanExecute(null))
            ViewModel.ToggleSystemResponsivenessCommand.Execute(null);
    }

    private void NetworkThrottling_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleNetworkThrottlingCommand.CanExecute(null))
            ViewModel.ToggleNetworkThrottlingCommand.Execute(null);
    }

    private void CoreUnparking_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleCoreUnparkingCommand.CanExecute(null))
            ViewModel.ToggleCoreUnparkingCommand.Execute(null);
    }

    private void MinProcessorState_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleMinProcessorStateCommand.CanExecute(null))
            ViewModel.ToggleMinProcessorStateCommand.Execute(null);
    }

    // ── MMAgent Toggles ──
    private async void MemoryCompression_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsMemoryCompressionEnabled)
        {
            await ViewModel.ToggleMemoryCompressionCommand.ExecuteAsync(sw.IsOn);
            if (sw.IsOn != ViewModel.IsMemoryCompressionEnabled)
                sw.IsOn = ViewModel.IsMemoryCompressionEnabled;
        }
    }

    private async void PageCombining_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsPageCombiningEnabled)
        {
            await ViewModel.TogglePageCombiningCommand.ExecuteAsync(sw.IsOn);
            if (sw.IsOn != ViewModel.IsPageCombiningEnabled)
                sw.IsOn = ViewModel.IsPageCombiningEnabled;
        }
    }

    private async void AppPreLaunch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsAppPreLaunchEnabled)
        {
            await ViewModel.ToggleAppPreLaunchCommand.ExecuteAsync(sw.IsOn);
            if (sw.IsOn != ViewModel.IsAppPreLaunchEnabled)
                sw.IsOn = ViewModel.IsAppPreLaunchEnabled;
        }
    }

    private async void AppLaunchProfiling_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsAppLaunchProfilingEnabled)
        {
            await ViewModel.ToggleAppLaunchProfilingCommand.ExecuteAsync(sw.IsOn);
            if (sw.IsOn != ViewModel.IsAppLaunchProfilingEnabled)
                sw.IsOn = ViewModel.IsAppLaunchProfilingEnabled;
        }
    }

    private async void OperationRecording_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsOperationRecordingEnabled)
        {
            await ViewModel.ToggleOperationRecordingCommand.ExecuteAsync(sw.IsOn);
            if (sw.IsOn != ViewModel.IsOperationRecordingEnabled)
                sw.IsOn = ViewModel.IsOperationRecordingEnabled;
        }
    }

    // ── PowerCfg Toggles ──
    private async void Hibernation_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsHibernationEnabled)
            await ViewModel.ToggleHibernationCommand.ExecuteAsync(sw.IsOn);
    }

    private async void FastStartupReduced_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsFastStartupReduced)
            await ViewModel.ToggleFastStartupReducedCommand.ExecuteAsync(sw.IsOn);
    }

    private async void ProcessorBoost_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsProcessorBoostUnhidden)
            await ViewModel.ToggleProcessorBoostUnhideCommand.ExecuteAsync(sw.IsOn);
    }

    private async void WakeDevice_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && sender is ToggleSwitch sw && sw.DataContext is PowerWakeDeviceItem item)
        {
            if (item.IsArmed != sw.IsOn)
            {
                item.IsArmed = sw.IsOn;
                await ViewModel.ToggleDeviceWakeCommand.ExecuteAsync(item);
            }
        }
    }

    private async void SetPlanActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is PowerPlanItem item)
        {
            await ViewModel.SetActivePowerPlanCommand.ExecuteAsync(item);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RegistryBackupItem item)
        {
            await ViewModel.RestoreBackupCommand.ExecuteAsync(item);
        }
    }

    private void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RegistryBackupItem item)
        {
            ViewModel.DeleteBackupCommand.Execute(item);
        }
    }

    private void Nagle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.ToggleNagleAlgorithmCommand.Execute(null);
        }
    }

    private void DisablePagingExecutive_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.ToggleDisablePagingExecutiveCommand.Execute(null);
        }
    }

    private void GroupPolicyTelemetry_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.ToggleGroupPolicyTelemetryCommand.Execute(null);
        }
    }

    private void SelectAllDrivers_Click(object sender, RoutedEventArgs e)
    {
        bool anyUnselected = ViewModel.OemDrivers.Any(d => !d.IsSelected);
        ViewModel.SelectAllDriversCommand.Execute(anyUnselected);
    }

    private async void DeleteDriverItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OemDriverItem item)
        {
            await ViewModel.DeleteDriverCommand.ExecuteAsync(item);
        }
    }
}
