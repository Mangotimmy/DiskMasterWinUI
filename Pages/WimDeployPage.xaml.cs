using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class WimDeployPage : Page
{
    public WimDeployViewModel ViewModel { get; } = new();

    public WimDeployPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.DeployLog))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    DeployLogScrollViewer?.ChangeView(null, DeployLogScrollViewer.ExtentHeight, null, true);
                });
            }
        };
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        PageHeaderTitle.Text = lang switch { "zh-CN" => "WIM/ESD 系统映像部署与 Windows 附加组件", "en-US" => "WIM/ESD System Image Deployment & Tweaks", "ja-JP" => "WIM/ESD システムイメージ展開", _ => "WIM/ESD 系統映像部署與 Windows 附加元件" };

        RufusModeRadio.Content = lang switch { "zh-CN" => "⚡ 微软官方直链", "en-US" => "⚡ Official Direct Link", "ja-JP" => "⚡ 公式ダイレクトリンク", _ => "⚡ 微軟官方直鏈" };
        MsdnModeRadio.Content = lang switch { "zh-CN" => "📦 经典官方发行库", "en-US" => "📦 Classic Official Catalog", "ja-JP" => "📦 公式クラシックカタログ", _ => "📦 經典官方發行庫" };
        RufusEngineHeaderTitle.Text = lang switch { "zh-CN" => "⚡ 微软官方原版直链引擎", "en-US" => "⚡ Official Direct Link Engine", "ja-JP" => "⚡ 公式ダイレクトリンクエンジン", _ => "⚡ 微軟官方原版直鏈引擎" };
        AutoFetchCheck.Content = lang switch { "zh-CN" => "选项变更时自动获取最新直链", "en-US" => "Auto-fetch direct link on selection change", "ja-JP" => "選択変更時に最新リンクを自動取得", _ => "選項變更時自動獲取最新直鏈" };
        RefreshVersionsText.Text = lang switch { "zh-CN" => "🔄 检查最新版本", "en-US" => "🔄 Check Latest Versions", "ja-JP" => "🔄 最新バージョンを確認", _ => "🔄 檢查最新版本" };

        RufusOsBox.Header = lang switch { "zh-CN" => "操作系统", "en-US" => "Operating System", "ja-JP" => "オペレーティングシステム", _ => "作業系統" };
        RufusArchBox.Header = lang switch { "zh-CN" => "架构", "en-US" => "Architecture", "ja-JP" => "アーキテクチャ", _ => "架構" };
        RufusReleaseBox.Header = lang switch { "zh-CN" => "版本号", "en-US" => "Release Build", "ja-JP" => "リリースビルド", _ => "版本號" };
        RufusLanguageBox.Header = lang switch { "zh-CN" => "官方语言", "en-US" => "Official Language", "ja-JP" => "公式言語", _ => "官方語言" };
        RufusDirectUrlBox.Header = lang switch { "zh-CN" => "微软官方原版直链", "en-US" => "Official Direct Download Link", "ja-JP" => "公式ダイレクトダウンロードリンク", _ => "微軟官方原版直鏈" };
        RufusDirectUrlBox.PlaceholderText = lang switch { "zh-CN" => "选择上方版本或点击下方按钮，由微软官方自动签发直链...", "en-US" => "Select version above or click button below to generate official link...", "ja-JP" => "上のバージョンを選択するかボタンをクリック...", _ => "選取上方版本或點擊下方按鈕，由微軟官方自動簽發直鏈..." };

        FilterAllBtn.Content = lang switch { "zh-CN" => "全部", "en-US" => "All", "ja-JP" => "すべて", _ => "全部" };
        FilterModernBtn.Content = lang switch { "zh-CN" => "现代", "en-US" => "Modern", "ja-JP" => "モダン", _ => "現代" };
        FilterClassicBtn.Content = lang switch { "zh-CN" => "经典", "en-US" => "Classic", "ja-JP" => "クラシック", _ => "經典" };
        FilterLegacyBtn.Content = lang switch { "zh-CN" => "旧版", "en-US" => "Legacy", "ja-JP" => "レガシー", _ => "舊版" };

        CatalogSelectBox.Header = lang switch { "zh-CN" => "选择要下载的 Windows 发行版", "en-US" => "Select Windows Edition to Download", "ja-JP" => "ダウンロードする Windows エディションを選択", _ => "選擇欲下載之 Windows 官方發行版" };
        DownloadDestFolderBox.Header = lang switch { "zh-CN" => "下载保存目录", "en-US" => "Download Destination Folder", "ja-JP" => "ダウンロード先フォルダ", _ => "下載儲存目錄" };

        ImagePathBox.Header = lang switch { "zh-CN" => "WIM / ESD / ISO 映像文件路径", "en-US" => "WIM / ESD / ISO Image Path", "ja-JP" => "WIM / ESD / ISO イメージパス", _ => "WIM / ESD / ISO 映像檔路徑" };
        ImagePathBox.PlaceholderText = lang switch { "zh-CN" => "请点选浏览或输入 .wim / .esd / .iso", "en-US" => "Browse or enter path to .wim / .esd / .iso", "ja-JP" => "参照または .wim / .esd / .iso のパスを入力", _ => "請點選瀏覽或輸入 .wim / .esd / .iso" };
        BrowseImageBtn.Content = lang switch { "zh-CN" => "📁 浏览...", "en-US" => "📁 Browse...", "ja-JP" => "📁 参照...", _ => "📁 瀏覽..." };
        ParseImageBtn.Content = lang switch { "zh-CN" => "🔍 解析", "en-US" => "🔍 Parse", "ja-JP" => "🔍 解析", _ => "🔍 解析" };

        IsoBadgeText.Text = lang switch { "zh-CN" => "已自动挂载 ISO 虚拟磁盘", "en-US" => "ISO Virtual Disk Auto-Mounted", "ja-JP" => "ISO仮想ディスクを自動マウント", _ => "已自動掛載 ISO 虛擬磁碟" };
        DismountIsoBtn.Content = lang switch { "zh-CN" => "⏏️ 卸载 ISO", "en-US" => "⏏️ Dismount ISO", "ja-JP" => "⏏️ ISO をアンマウント", _ => "⏏️ 卸載 ISO" };

        EditionBox.Header = lang switch { "zh-CN" => "安装版本与索引", "en-US" => "Target Edition & Index", "ja-JP" => "対象エディションとインデックス", _ => "安裝版本與索引" };
        TargetDriveBox.Header = lang switch { "zh-CN" => "目标安装分区", "en-US" => "Target Drive (OS)", "ja-JP" => "対象ドライブ (OS)", _ => "目標安裝磁區" };
        BootDriveBox.Header = lang switch { "zh-CN" => "引导分区", "en-US" => "Boot ESP Drive", "ja-JP" => "ブート ESP ドライブ", _ => "引導磁區" };
        FirmwareBox.Header = lang switch { "zh-CN" => "固件引导模式", "en-US" => "Firmware Mode", "ja-JP" => "ファームウェアモード", _ => "韌體開機引導模式" };

        AddonsTitle.Text = lang switch { "zh-CN" => "Windows 附加组件与绕过选项", "en-US" => "Windows Addons & Bypass Options", "ja-JP" => "Windows アドオンとバイパスオプション", _ => "Windows 附加元件與繞過選項" };
        BypassWin11Check.Content = lang switch { "zh-CN" => "🛡️ 一键绕过 Win11 硬件限制", "en-US" => "🛡️ Bypass Windows 11 Requirements", "ja-JP" => "🛡️ Windows 11 要件をバイパス", _ => "🛡️ 一鍵繞過 Win11 硬體限制" };
        BypassNroCheck.Content = lang switch { "zh-CN" => "🌐 绕过微软账户强制联网", "en-US" => "🌐 Bypass Microsoft Account Requirement", "ja-JP" => "🌐 Microsoft アカウント強制をバイパス", _ => "🌐 繞過微軟帳戶強制聯網" };
        CompactOsCheck.Content = lang switch { "zh-CN" => "⚡ Compact OS 紧凑压缩部署", "en-US" => "⚡ Compact OS Compression", "ja-JP" => "⚡ Compact OS 圧縮展開", _ => "⚡ Compact OS 緊湊壓縮部署" };
        AutoBootCheck.Content = lang switch { "zh-CN" => "🚀 部署后自动生成 BCD 引导文件", "en-US" => "🚀 Auto-Generate BCD Boot Files", "ja-JP" => "🚀 展開後に BCD ブートファイルを自動生成", _ => "🚀 部署後自動生成 BCD 開機引導檔" };
        InjectWin7DriversCheck.Content = lang switch { "zh-CN" => "⚡ 自动注入 Windows 7 USB 3.0 与 NVMe 驱动", "en-US" => "⚡ Auto-Inject Windows 7 USB 3.0 & NVMe Drivers", "ja-JP" => "⚡ Windows 7 USB 3.0 と NVMe ドライバーを自動注入", _ => "⚡ 自動注入 Windows 7 USB 3.0 與 NVMe 驅動" };

        DriverFolderBox.Header = lang switch { "zh-CN" => "离线驱动注入目录 (可选)", "en-US" => "Inject Drivers Folder (Optional)", "ja-JP" => "オフラインドライバー注入フォルダ (省略可)", _ => "離線驅動注入目錄 (選填)" };
        DriverFolderBox.PlaceholderText = lang switch { "zh-CN" => "包含 .inf 驱动的文件夹 (如 NVMe、WiFi)", "en-US" => "Folder containing .inf drivers (e.g. NVMe, WiFi)", "ja-JP" => ".inf ドライバーを含むフォルダ (例: NVMe、WiFi)", _ => "包含 .inf 驅動之資料夾 (如 NVMe、WiFi)" };
        BrowseDriverBtn.Content = lang switch { "zh-CN" => "📂 浏览...", "en-US" => "📂 Browse...", "ja-JP" => "📂 参照...", _ => "📂 瀏覽..." };

        StartDeployBtnText.Text = lang switch { "zh-CN" => "🚀 开始部署 Windows 系统映像", "en-US" => "🚀 Start Windows System Deployment", "ja-JP" => "🚀 Windows システムイメージ展開を開始", _ => "🚀 開始部署 Windows 系統映像" };
        DeployLogTitle.Text = lang switch { "zh-CN" => "部署终端日志", "en-US" => "Deployment Output Console", "ja-JP" => "展開コンソールログ", _ => "部署終端日誌" };
        CopyDeployLogBtn.Content = lang switch { "zh-CN" => "📋 复制日志", "en-US" => "📋 Copy Log", "ja-JP" => "📋 ログをコピー", _ => "📋 複製日誌" };
        ClearDeployLogBtn.Content = lang switch { "zh-CN" => "清除", "en-US" => "Clear", "ja-JP" => "消去", _ => "清除" };

        ImageMgmtExpander.Header = lang switch { "zh-CN" => "🔄 映像管理工具", "en-US" => "🔄 Image Management Tools", "ja-JP" => "🔄 イメージ管理ツール", _ => "🔄 映像管理工具" };
        CaptureTitle.Text = lang switch { "zh-CN" => "📸 备份磁盘为 WIM 映像", "en-US" => "📸 Capture Disk to WIM Image", "ja-JP" => "📸 ディスクを WIM イメージにキャプチャ", _ => "📸 備份磁碟為 WIM 映像" };
        CaptureDirBox.Header = lang switch { "zh-CN" => "来源目录", "en-US" => "Source Directory", "ja-JP" => "ソースディレクトリ", _ => "來源目錄" };
        CaptureOutputBox.Header = lang switch { "zh-CN" => "输出 WIM 路径", "en-US" => "Output WIM Path", "ja-JP" => "出力 WIM パス", _ => "輸出 WIM 路徑" };
        CaptureNameBox.Header = lang switch { "zh-CN" => "映像名称", "en-US" => "Image Name", "ja-JP" => "イメージ名", _ => "映像名稱" };
        CaptureImageBtn.Content = lang switch { "zh-CN" => "📸 开始备份", "en-US" => "📸 Start Capture", "ja-JP" => "📸 キャプチャ開始", _ => "📸 開始備份" };
        ExportTitle.Text = lang switch { "zh-CN" => "🔄 ESD ↔ WIM 格式转换", "en-US" => "🔄 ESD ↔ WIM Format Conversion", "ja-JP" => "🔄 ESD ↔ WIM 形式変換", _ => "🔄 ESD ↔ WIM 格式轉換" };
        ExportDestBox.Header = lang switch { "zh-CN" => "输出文件路径", "en-US" => "Output File Path", "ja-JP" => "出力ファイルパス", _ => "輸出檔案路徑" };
        ExportImageBtn.Content = lang switch { "zh-CN" => "🔄 开始转换", "en-US" => "🔄 Start Conversion", "ja-JP" => "🔄 変換開始", _ => "🔄 開始轉換" };
        SplitTitle.Text = lang switch { "zh-CN" => "✂️ 分割 WIM 至 FAT32 USB", "en-US" => "✂️ Split WIM for FAT32 USB", "ja-JP" => "✂️ WIM を FAT32 USB 向けに分割", _ => "✂️ 分割 WIM 至 FAT32 USB" };
        SplitOutputBox.Header = lang switch { "zh-CN" => "输出 SWM 路径", "en-US" => "Output SWM Path", "ja-JP" => "出力 SWM パス", _ => "輸出 SWM 路徑" };
        SplitImageBtn.Content = lang switch { "zh-CN" => "✂️ 开始分割", "en-US" => "✂️ Start Split", "ja-JP" => "✂️ 分割開始", _ => "✂️ 開始分割" };
        MountWimTitle.Text = lang switch { "zh-CN" => "📂 挂载 WIM 映像", "en-US" => "📂 Mount/Unmount WIM Image", "ja-JP" => "📂 WIM イメージのマウント", _ => "📂 掛載 WIM 映像" };
        MountDirBox.Header = lang switch { "zh-CN" => "挂载目录", "en-US" => "Mount Directory", "ja-JP" => "マウントディレクトリ", _ => "掛載目錄" };
        MountWimBtn.Content = lang switch { "zh-CN" => "📂 挂载 WIM", "en-US" => "📂 Mount WIM", "ja-JP" => "📂 WIM をマウント", _ => "📂 掛載 WIM" };
        UnmountWimBtn.Content = lang switch { "zh-CN" => "⏏️ 卸载", "en-US" => "⏏️ Unmount (Commit)", "ja-JP" => "⏏️ アンマウント (コミット)", _ => "⏏️ 卸載" };
        CleanupMountsBtn.Content = lang switch { "zh-CN" => "🧹 清理孤立挂载", "en-US" => "🧹 Cleanup Orphaned Mounts", "ja-JP" => "🧹 孤立マウントのクリーンアップ", _ => "🧹 清理孤立掛載" };
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ApplyLanguage();
        ViewModel.RefreshAvailableDrives();
    }
}
