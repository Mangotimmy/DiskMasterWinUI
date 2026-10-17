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
        TabExplorerShell.Header = lang switch { "zh-CN" => "🗂️ 文件资源管理器、右键与图标", "en-US" => "🗂️ Explorer, Context Menu & Icons", "ja-JP" => "🗂️ エクスプローラー、右クリックとアイコン", _ => "🗂️ 檔案總管、右鍵與圖示" };
        TabBackup.Header = lang switch { "zh-CN" => "💾 注册表备份与还原", "en-US" => "💾 Registry Backup & Restore", "ja-JP" => "💾 レジストリバックアップと復元", _ => "💾 登錄檔備份與還原" };
        TabOneDrive.Header = lang switch { "zh-CN" => "☁️ OneDrive 深度治理与卸载", "en-US" => "☁️ OneDrive Governance & Uninstall", "ja-JP" => "☁️ OneDrive 管理と削除", _ => "☁️ OneDrive 深度治理與卸載" };
        TabDrivers.Header = lang switch { "zh-CN" => "🏎️ OEM 驱动清理 (PnPUtil)", "en-US" => "🏎️ OEM Driver Cleanup (PnPUtil)", "ja-JP" => "🏎️ OEM ドライバの削除 (PnPUtil)", _ => "🏎️ OEM 驅動清理 (PnPUtil)" };

        HeroTitle.Text = lang switch { "zh-CN" => "电竞极致模式", "en-US" => "Ultra Gaming Mode (1-Click)", "ja-JP" => "ウルトラゲーミングモード", _ => "電競遊戲極致模式" };
        HeroSubtitle.Text = lang switch
        {
            "zh-CN" => "一键同步调校：CPU 量子时间分配 (0x26)、启用终极效能电源计划、优化前台算力保留 (SystemResponsiveness=10)、关闭网络节流防 Ping 突增、关闭 Game DVR 背景录制、开启 HAGS 硬件加速与 1:1 传感器零延迟。",
            "en-US" => "1-Click optimization: CPU Quantum Scheduling (0x26), Ultimate Performance Power Plan, Foreground CPU Priority (SystemResponsiveness=10), Network Throttling Disabled, Game DVR Disabled, HAGS enabled, and 1:1 input raw tracking.",
            "ja-JP" => "ワンクリック最適化：CPU量子スケジューリング (0x26)、究極パフォーマンス電源プラン、フォアグラウンドCPU優先 (SystemResponsiveness=10)、ネットワーク絞りを無効化、Game DVR無効、HAGS有効、1:1センサー追跡。",
            _ => "一鍵同步調校：CPU 量子時間分配 (0x26)、啟用終極效能電源計畫、優化前台算力保留 (SystemResponsiveness=10)、關閉網路節流防 Ping 突增、關閉 Game DVR 背景錄影負擔、開啟 HAGS 硬體加速與 1:1 感應器零延遲。"
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

        SystemResponsivenessLabel.Text = lang switch { "zh-CN" => "前台游戏 CPU 算力保留优化 (SystemResponsiveness = 10% 或 0%)", "en-US" => "Foreground Game CPU Reservation (SystemResponsiveness = 10% or 0%)", "ja-JP" => "フォアグラウンド ゲーム CPU 割り当て最適化 (SystemResponsiveness = 10% / 0%)", _ => "前台遊戲 CPU 算力保留優化 (SystemResponsiveness = 10% 或 0%)" };
        SystemResponsivenessDesc.Text = lang switch { "zh-CN" => "Windows 原生预留 20% CPU 给低优先级背景服务。EvolveOpt 推荐设为 10% (十进制) 兼顾语音/串流防爆音，或 0% 极限全给游戏。", "en-US" => "Windows reserves 20% CPU for background tasks by default. Set to 10% (decimal) for optimal balance without audio crackle, or 0% for extreme gaming.", "ja-JP" => "Windowsは標準で20%のCPUをバックグラウンド用に予約します。推奨は10%（十進数、音声途切れ防止）、極限ゲーミングは0%です。", _ => "Windows 原生預留 20% CPU 給低優先權背景服務。EvolveOpt 推薦設為 10% (十進位) 兼顧語音/串流防爆音，或 0% 極限全給遊戲。" };

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

        // Advanced Tweaks Card Localization (Tab 2)
        AdvTweaksCardTitle.Text = lang switch { "zh-CN" => "⚡ 深度低延迟注册表与组策略优化", "en-US" => "⚡ Low-Latency Registry & Group Policy Tweaks", "ja-JP" => "⚡ 低遅延レジストリ & グループポリシー最適化", _ => "⚡ 深度低延遲登錄檔與群組原則優化" };
        NagleLabel.Text = lang switch { "zh-CN" => "禁用 Nagle 算法 (TcpAckFrequency = 1 & TCPNoDelay = 1)", "en-US" => "Disable Nagle's Algorithm (TcpAckFrequency = 1 & TCPNoDelay = 1)", "ja-JP" => "Nagle アルゴリズムを無効化 (TcpAckFrequency = 1 & TCPNoDelay = 1)", _ => "停用 Nagle 演算法 (TcpAckFrequency = 1 & TCPNoDelay = 1)" };
        NagleDesc.Text = lang switch { "zh-CN" => "强制网卡立即发送 TCP 数据包，消除排队等待延迟，大幅降低网游 Ping 与操作抖动。", "en-US" => "Forces network adapter to send TCP packets immediately without buffering delay.", "ja-JP" => "TCPパケットの即時送信を強制し、オンラインゲームのPingとジッターを低減します。", _ => "強制網卡立刻發送 TCP 封包，不進行佇列合併等待，大幅降低線上遊戲連線 Ping 與操作抖動。" };
        PagingExecLabel.Text = lang switch { "zh-CN" => "内核与驱动锁定于物理内存 (DisablePagingExecutive = 1)", "en-US" => "Lock Kernel & Drivers in RAM (DisablePagingExecutive = 1)", "ja-JP" => "カーネルとドライバを物理メモリに固定 (DisablePagingExecutive = 1)", _ => "核心與驅動鎖定於實體 RAM (DisablePagingExecutive = 1)" };
        PagingExecDesc.Text = lang switch { "zh-CN" => "禁止 Windows 将系统内核代码与驱动程序分页写入硬盘虚拟内存，确保 100% 在高速 RAM 中运行。", "en-US" => "Prevents paging system kernel and device drivers to disk, keeping them in high-speed RAM.", "ja-JP" => "システムカーネルとドライバがディスクにページングされるのを防ぎ、高速RAMで常駐させます。", _ => "禁止 Windows 將核心系統代碼與驅動程式分頁寫入虛擬記憶體硬碟，確保 100% 於高速 RAM 運行。" };
        GroupPolicyLabel.Text = lang switch { "zh-CN" => "本地组策略隐私防范 (关闭遥测/开始菜单Bing/P2P上传)", "en-US" => "Group Policy Privacy & Telemetry Guard", "ja-JP" => "グループポリシー プライバシー保護 (テレメトリ/検索無効化)", _ => "本機群組原則隱私防護 (關閉遙測/開始功能表Bing/P2P上傳)" };
        GroupPolicyDesc.Text = lang switch { "zh-CN" => "关闭 AllowTelemetry、禁用开始菜单网络搜索、关闭传递优化 P2P 后台占用。", "en-US" => "Disables telemetry, start menu web search, and delivery optimization P2P uploads.", "ja-JP" => "テレメトリ、スタートメニューWeb検索、配信最適化P2Pアップロードを停止します。", _ => "關閉 AllowTelemetry、停用開始功能表網路搜尋、關閉傳遞最佳化 P2P 背景佔用。" };
        DpsServiceLabel.Text = lang switch { "zh-CN" => "禁用 DPS 诊断策略服务防内存膨胀 (DPS Start = 4)", "en-US" => "Disable Diagnostic Policy Service (DPS Start = 4)", "ja-JP" => "DPS 診断ポリシーサービス無効化 (DPS Start = 4)", _ => "停用 DPS 診斷原則服務防記憶體膨脹 (DPS Start = 4)" };
        DpsServiceDesc.Text = lang switch { "zh-CN" => "设置 Start = 4 (Disabled) 并停止 DPS 服务，防止背景故障排除引发 svchost 内存无限膨胀与 CPU 尖峰。", "en-US" => "Stops and disables DPS to prevent svchost memory leak bloat and background CPU spikes.", "ja-JP" => "DPSサービスを停止・無効化し、svchostのメモリリーク膨張とCPUスパイクを防止します。", _ => "設定 Start = 4 (Disabled) 並停止 Diagnostic Policy Service，防止背景疑難排解監控引發 svchost 記憶體無限膨脹與 CPU 尖峰。" };
        L2tpVpnLabel.Text = lang switch { "zh-CN" => "L2TP/IPsec VPN NAT 穿透修复 (AssumeUDPEncapsulationContextOnSendRule = 2)", "en-US" => "L2TP/IPsec VPN NAT Traversal Fix (AssumeUDPEncapsulation = 2)", "ja-JP" => "L2TP/IPsec VPN NATトラバーサル修復 (AssumeUDPEncapsulation = 2)", _ => "L2TP/IPsec VPN NAT 穿透連線修復 (AssumeUDPEncapsulationContextOnSendRule = 2)" };
        L2tpVpnDesc.Text = lang switch { "zh-CN" => "在 PolicyAgent 建立 AssumeUDPEncapsulationContextOnSendRule = 2，彻底解决路由器 NAT 后的 VPN 连线错误 (789/809)。", "en-US" => "Resolves VPN error 789/809 behind NAT routers by allowing UDP encapsulation for IPsec.", "ja-JP" => "NATルーター背後のVPN接続エラー (789/809) を解消するため UDP カプセル化を有効化します。", _ => "在 PolicyAgent 建立 REG_DWORD 值 AssumeUDPEncapsulationContextOnSendRule = 2，徹底解決位於路由器 NAT 後方時 L2TP/IPsec VPN 無法連線 (錯誤 789 / 809) 問題。修改後重開機生效。" };
        DiagTrackCardTitle.Text = lang switch { "zh-CN" => "DiagTrack 遥测追踪服务控制 (Connected User Experiences and Telemetry)", "en-US" => "DiagTrack Telemetry Service Control", "ja-JP" => "DiagTrack テレメトリ追跡サービス制御", _ => "DiagTrack 遙測追蹤服務控制 (Connected User Experiences and Telemetry)" };
        DiagTrackCardDesc.Text = lang switch { "zh-CN" => "彻底停止并禁用微软背景诊断遥测服务，释放背景 I/O 与网络带宽。Windows Update 仍可正常更新。", "en-US" => "Stops and disables Microsoft diagnostic telemetry service, saving background disk I/O and bandwidth.", "ja-JP" => "Microsoft 診断テレメトリサービスを停止・無効化し、ディスクI/Oと帯域幅を解放します。", _ => "一鍵執行 `sc stop DiagTrack & sc config DiagTrack start=disabled` 徹底關閉微軟背景診斷追蹤服務，釋放背景 I/O 與頻寬。關閉後 Windows Update 依然完全正常運作！" };

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

        // Tab Headers Localization
        TabGaming.Header = lang switch { "zh-CN" => "🎮 电竞游戏模式 (Gaming Mode)", "en-US" => "🎮 Ultra Gaming Mode", "ja-JP" => "🎮 ゲーミングモード (Gaming Mode)", _ => "🎮 電競遊戲模式 (Gaming Mode)" };
        TabCpu.Header = lang switch { "zh-CN" => "⚡ CPU 与程序调度 (CPU & Task)", "en-US" => "⚡ CPU & Quantum Scheduling", "ja-JP" => "⚡ CPU & スケジューリング", _ => "⚡ CPU 與程序排程調優 (CPU & Task)" };
        TabPower.Header = lang switch { "zh-CN" => "🔋 电源计划与唤醒 (Power Plans)", "en-US" => "🔋 Power Plans & Wake", "ja-JP" => "🔋 電源プラン & 復帰管理", _ => "🔋 電源計畫與極速喚醒 (Power Plans)" };
        TabExplorerShell.Header = lang switch { "zh-CN" => "🗂️ 资源管理器、右键与图标 (Explorer & Icons)", "en-US" => "🗂️ Explorer, Shell & Icons", "ja-JP" => "🗂️ エクスプローラー & アイコン", _ => "🗂️ 檔案總管、右鍵與圖示 (Explorer & Icons)" };
        TabBackup.Header = lang switch { "zh-CN" => "💾 注册表备份与还原 (Backup & Restore)", "en-US" => "💾 Registry Backup & Restore", "ja-JP" => "💾 レジストリバックアップ & 復元", _ => "💾 登錄檔備份與還原 (Backup & Restore)" };
        TabOneDrive.Header = lang switch { "zh-CN" => "☁️ OneDrive 深度治理与卸载", "en-US" => "☁️ OneDrive Governance & Removal", "ja-JP" => "☁️ OneDrive アンインストール & 管理", _ => "☁️ OneDrive 深度治理與卸載" };
        TabDrivers.Header = lang switch { "zh-CN" => "🏎️ OEM 驱动清理 (PnPUtil)", "en-US" => "🏎️ OEM Drivers Cleanup (PnPUtil)", "ja-JP" => "🏎️ OEM ドライバクリーンアップ", _ => "🏎️ OEM 驅動清理 (PnPUtil)" };

        // Tab 3.5: Explorer & Shell Localization
        WatchdogCardTitle.Text = lang switch { "zh-CN" => "🛡️ 资源管理器后台自动守护与救援服务 (Auto-Recovery Watchdog)", "en-US" => "🛡️ Explorer Auto-Recovery Watchdog Service", "ja-JP" => "🛡️ エクスプローラー自動復帰ウォッチドッグサービス", _ => "🛡️ Explorer.exe 檔案總管背景自動守護與救援服務 (Auto-Recovery Watchdog)" };
        WatchdogCardDesc.Text = lang switch { "zh-CN" => "当 Windows 资源管理器 (explorer.exe) 崩溃、无响应或异常终止时，背景守护服务将自动侦测并秒级重启恢复桌面与任务栏。", "en-US" => "When Windows Explorer crashes or freezes, the background watchdog automatically detects and restarts it in seconds.", "ja-JP" => "エクスプローラーがクラッシュまたはフリーズした際、バックグラウンドで即座に検知し自動復帰します。", _ => "當 Windows 檔案總管 (explorer.exe) 因外掛衝突崩潰、無回應或被意外終止導致黑畫面與工作列消失時，背景守護服務將自動偵測並秒速重啟恢復桌面。" };
        ContextMenuCardTitle.Text = lang switch { "zh-CN" => "🖱️ 右键菜单版面切换与延迟修复 (Context Menu Layout & Delay Fix)", "en-US" => "🖱️ Context Menu Layout Switcher & Latency Fix", "ja-JP" => "🖱️ 右クリックメニューのレイアウト切替 & 遅延解消", _ => "🖱️ 右鍵選單版面切換與右鍵轉圈卡頓修復 (Context Menu Layout & Delay Fix)" };
        ContextMenuCardDesc.Text = lang switch { "zh-CN" => "一键在「Windows 10 经典完整右键菜单」与「Windows 11 现代折叠菜单」之间自由切换，并修复右键转圈延迟。", "en-US" => "Easily toggle between classic full Windows 10 context menu and modern Windows 11 menu, fixing delay stutters.", "ja-JP" => "Windows 10 クラシックメニューと Windows 11 モダンメニューを切り替え、右クリック時の遅延を解消します。", _ => "一鍵在「Windows 10 經典完整右鍵選單 (直接顯示 7-Zip/解壓縮/重新命名等完整功能)」與「Windows 11 現代簡潔選單」之間自由切換，並修復右鍵轉圈延遲。" };
        FolderOptCardTitle.Text = lang switch { "zh-CN" => "📂 文件夹载入加速优化与文件夹图片 (Folder Customize & Slow Loading Fix)", "en-US" => "📂 Folder Loading Acceleration & Folder Picture", "ja-JP" => "📂 フォルダ読み込み高速化 & フォルダ画像設定", _ => "📂 資料夾載入加速最佳化與資料夾圖片設定 (Folder Customize & Slow Loading Fix)" };
        FolderOptCardDesc.Text = lang switch { "zh-CN" => "解决下载区或大型文件夹打开时上方绿条缓慢读条、卡死转圈问题。支持针对指定文件夹与子文件夹应用优化模板与封面。", "en-US" => "Resolves slow green loading bars in heavy folders. Supports applying General Items template and custom pictures to folders.", "ja-JP" => "重いフォルダを開く際の緑のローディングバーの停滞を解消。一般項目テンプレートとカバー画像を適用します。", _ => "解決下載區或大型資料夾開啟時上方綠條緩慢讀條、卡死轉圈問題。支援全域鎖定「一般項目 (General items)」或針對指定資料夾與所有子資料夾套用最佳化範本與封面圖片。" };
        CustomIconsCardTitle.Text = lang switch { "zh-CN" => "🎨 文件夹与文件类型自定义图标中心", "en-US" => "🎨 Custom Icons for Folders & File Types", "ja-JP" => "🎨 フォルダ・ファイル形式のカスタムアイコン", _ => "🎨 資料夾與檔案類型自訂圖示中心 (Custom Icons for Folders & File Types)" };
        CustomIconsCardDesc.Text = lang switch { "zh-CN" => "为目标文件夹或任意文件扩展名更换专属精美图标，支持内建 Windows 精选图标库与外部 .ico/.exe/.dll 文件。", "en-US" => "Customize icons for target folders or file extensions with built-in or external .ico/.dll/.exe resources.", "ja-JP" => "対象フォルダやファイル拡張子に専用アイコンを割り当てます。内蔵アイコンや .ico/.dll/.exe に対応。", _ => "為上方的「目標資料夾」或任意「檔案副檔名 (例如 .txt / .mp4 / .log)」更換專屬精美圖示，支援內建 Windows 精選圖示庫與外部 .ico / .exe / .dll 圖示檔。" };
        FolderIconSectionTitle.Text = lang switch { "zh-CN" => "📁 文件夹图标 (应用至选定目标文件夹)", "en-US" => "📁 Folder Icons (Apply to Selected Target Folder)", "ja-JP" => "📁 フォルダアイコン (選択中フォルダに適用)", _ => "📁 資料夾圖示 (Folder icons - 套用至上方選取的目標資料夾)" };
        FileExtensionBox.Header = lang switch { "zh-CN" => "文件扩展名", "en-US" => "File Extension", "ja-JP" => "ファイル拡張子", _ => "檔案副檔名" };

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

    private bool _hasLoadedOnce;

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressToggled = true;
        try
        {
            ApplyLanguage();
            if (!_hasLoadedOnce)
            {
                _hasLoadedOnce = true;
                await ViewModel.RefreshAllCommand.ExecuteAsync(null);
            }
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

    private async void SetResponsiveness20_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetSystemResponsivenessValueCommand.ExecuteAsync(20);
    }

    private async void SetResponsiveness10_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetSystemResponsivenessValueCommand.ExecuteAsync(10);
    }

    private async void SetResponsiveness0_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SetSystemResponsivenessValueCommand.ExecuteAsync(0);
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

    private void DpsService_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleDpsServiceCommand.CanExecute(null))
        {
            ViewModel.ToggleDpsServiceCommand.Execute(null);
        }
    }

    private void L2tpVpnNatFix_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically && ViewModel.ToggleL2tpVpnNatFixCommand.CanExecute(null))
        {
            ViewModel.ToggleL2tpVpnNatFixCommand.Execute(null);
        }
    }

    private void ExplorerWatchdog_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.ToggleExplorerWatchdogCommand.Execute(null);
        }
    }

    private void GlobalFastFolder_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.ToggleGlobalFastFolderDiscoveryCommand.Execute(null);
        }
    }

    private void PopularIconCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressToggled && !ViewModel.IsUpdatingProgrammatically)
        {
            ViewModel.SelectPopularIconPresetCommand.Execute(null);
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

    private void OptimizerTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(OptimizerTabView.SelectedItem, TabOneDrive))
        {
            ViewModel.RefreshOneDriveStatusCommand.Execute(null);
        }
        else if (ReferenceEquals(OptimizerTabView.SelectedItem, TabDrivers) && ViewModel.OemDrivers.Count == 0 && !ViewModel.IsLoadingDrivers)
        {
            ViewModel.RefreshDriversCommand.Execute(null);
        }
        else if (ReferenceEquals(OptimizerTabView.SelectedItem, TabExplorerShell))
        {
            ViewModel.IsExplorerCurrentlyAlive = ExplorerWatchdogService.CheckExplorerRunning();
            ViewModel.IsExplorerWatchdogEnabled = ExplorerWatchdogService.Instance.IsEnabled;
            ViewModel.ExplorerWatchdogRecoverySummary = ExplorerWatchdogService.Instance.LastRecoveryTimeText;
        }
    }
}

