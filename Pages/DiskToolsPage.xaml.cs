using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using DiskMasterWinUI.ViewModels;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Pages;

public sealed partial class DiskToolsPage : Page
{
    public DiskToolsViewModel ViewModel { get; } = new();

    public DiskToolsPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ApplyLanguage();
        LocalizationService.Instance.LanguageChanged += ApplyLanguage;
    }

    public void ApplyLanguage()
    {
        var lang = LocalizationService.Instance.CurrentLanguage;
        RefreshToolsBtnText.Text = lang switch
        {
            "zh-CN" => "刷新工具与硬件信息",
            "en-US" => "Refresh Tools & Hardware Info",
            "ja-JP" => "ツールとハードウェア情報を更新",
            _ => "重新整理工具與硬體資訊"
        };
        TabKeys.Header = lang switch
        {
            "zh-CN" => "密钥与硬件序列号",
            "en-US" => "Keys & Serials",
            "ja-JP" => "プロダクトキーとシリアル",
            _ => "金鑰與硬體序號"
        };
        TabEnv.Header = lang switch
        {
            "zh-CN" => "环境检查与套件修复",
            "en-US" => "Environment Doctor",
            "ja-JP" => "環境診断とパッケージ修復",
            _ => "環境健檢與套件補齊"
        };
        TabOptimize.Header = lang switch
        {
            "zh-CN" => "高级维护与救援",
            "en-US" => "Optimization & Recovery",
            "ja-JP" => "高度メンテナンスと救急",
            _ => "進階維護與救援"
        };
        TabVhdx.Header = lang switch
        {
            "zh-CN" => "💽 VHD/VHDX 虚拟磁盘与缓存清理",
            "en-US" => "💽 VHD/VHDX & Cache Cleanup",
            "ja-JP" => "💽 VHD/VHDX 仮想ディスクとキャッシュ消去",
            _ => "💽 VHD/VHDX 虛擬磁碟與快取清理"
        };

        // Disk Health Tab
        TabHealth.Header = lang switch
        {
            "zh-CN" => "🩺 磁盘健康",
            "en-US" => "🩺 Disk Health",
            "ja-JP" => "🩺 ディスク健康診断",
            _ => "🩺 磁碟健康"
        };
        SmartTitle.Text = lang switch
        {
            "zh-CN" => "S.M.A.R.T. 磁盘健康监控",
            "en-US" => "S.M.A.R.T. Disk Health Monitor",
            "ja-JP" => "S.M.A.R.T. ディスク健康モニター",
            _ => "S.M.A.R.T. 磁碟健康監控"
        };
        RefreshHealthBtn.Content = lang switch
        {
            "zh-CN" => "🔄 重新扫描",
            "en-US" => "🔄 Refresh",
            "ja-JP" => "🔄 再スキャン",
            _ => "🔄 重新掃描"
        };
        SmartPredictionBtn.Content = lang switch
        {
            "zh-CN" => "🔮 预测性故障检测",
            "en-US" => "🔮 Failure Prediction",
            "ja-JP" => "🔮 予測的障害検出",
            _ => "🔮 預測性故障偵測"
        };
        ReliabilityExpander.Header = lang switch
        {
            "zh-CN" => "📊 详细可靠性指标",
            "en-US" => "📊 Reliability Counters",
            "ja-JP" => "📊 詳細信頼性カウンタ",
            _ => "📊 詳細可靠性指標"
        };
        ChkdskTitle.Text = lang switch
        {
            "zh-CN" => "chkdsk 文件系统完整性检查",
            "en-US" => "chkdsk Filesystem Integrity Check",
            "ja-JP" => "chkdsk ファイルシステム整合性チェック",
            _ => "chkdsk 檔案系統完整性檢查"
        };
        ChkdskDriveBox.Header = lang switch
        {
            "zh-CN" => "磁盘驱动器",
            "en-US" => "Drive",
            "ja-JP" => "ドライブ",
            _ => "磁碟機"
        };
        ChkdskScanBtn.Content = lang switch
        {
            "zh-CN" => "🔍 在线扫描",
            "en-US" => "🔍 Online Scan",
            "ja-JP" => "🔍 オンラインスキャン",
            _ => "🔍 線上掃描"
        };
        ChkdskSpotFixBtn.Content = lang switch
        {
            "zh-CN" => "🔧 快速修复",
            "en-US" => "🔧 Spot Fix",
            "ja-JP" => "🔧 スポット修復",
            _ => "🔧 快速修復"
        };
        ChkdskFixBtn.Content = lang switch
        {
            "zh-CN" => "🛠️ 完整修复",
            "en-US" => "🛠️ Full Fix",
            "ja-JP" => "🛠️ 完全修復",
            _ => "🛠️ 完整修復"
        };
        ChkdskFullBtn.Content = lang switch
        {
            "zh-CN" => "🩹 坏道修复",
            "en-US" => "🩹 Bad Sector Repair",
            "ja-JP" => "🩹 不良セクタ修復",
            _ => "🩹 壞軌修復"
        };
        ChkdskDirtyBtn.Content = "❓ Dirty Bit";
        ChkdskBootBtn.Content = lang switch
        {
            "zh-CN" => "📅 计划开机检查",
            "en-US" => "📅 Schedule Boot Check",
            "ja-JP" => "📅 起動時チェックをスケジュール",
            _ => "📅 排程開機檢查"
        };
        ChkdskCancelBtn.Content = lang switch
        {
            "zh-CN" => "⏹️ 停止",
            "en-US" => "⏹️ Stop",
            "ja-JP" => "⏹️ 停止",
            _ => "⏹️ 停止"
        };

        // Tab 1: Keys & Serials
        BiosOemKeyTitle.Text = lang switch { "zh-CN" => "UEFI / BIOS OEM 原厂嵌入密钥 (MSDM Table)", "en-US" => "UEFI / BIOS OEM Factory Key (MSDM Table)", "ja-JP" => "UEFI / BIOS OEM 工場出荷時キー (MSDM Table)", _ => "UEFI / BIOS OEM 原廠嵌入金鑰 (MSDM Table)" };
        InstalledKeyTitle.Text = lang switch { "zh-CN" => "当前系统激活密钥", "en-US" => "Installed Windows Product Key", "ja-JP" => "インストール済み Windows プロダクトキー", _ => "目前系統啟動金鑰" };
        HardwareSerialsTitle.Text = lang switch { "zh-CN" => "主板与 BIOS 实体硬件序列号", "en-US" => "Motherboard & BIOS Hardware Identification", "ja-JP" => "マザーボードとBIOSハードウェア識別", _ => "主機板與 BIOS 實體硬體序號" };
        BiosSerialBox.Header = lang switch { "zh-CN" => "BIOS 序列号", "en-US" => "BIOS Serial Number", "ja-JP" => "BIOS シリアル番号", _ => "BIOS 序號" };
        MotherboardProductBox.Header = lang switch { "zh-CN" => "主板型号", "en-US" => "Motherboard Model", "ja-JP" => "マザーボード型番", _ => "主機板型號" };
        ManufacturerBox.Header = lang switch { "zh-CN" => "原厂制造商", "en-US" => "Manufacturer", "ja-JP" => "製造元", _ => "原廠製造商" };
        WindowsProductNameBox.Header = lang switch { "zh-CN" => "Windows 版本名称", "en-US" => "Windows Edition Name", "ja-JP" => "Windows エディション名", _ => "Windows 版本名稱" };
        OfflineKeyTitle.Text = lang switch { "zh-CN" => "离线 Windows 密钥提取器", "en-US" => "Offline Windows Key Extractor", "ja-JP" => "オフライン Windows キー抽出ツール", _ => "離線 Windows 金鑰提取器" };
        OfflineKeyDesc.Text = lang switch
        {
            "zh-CN" => "在 WinPE 或双系统下，指定无法启动的副分区路径（如 D:\\Windows），直接解码提取原系统密钥。",
            "en-US" => "In WinPE or dual boot, specify the non-booting Windows path (e.g. D:\\Windows) to extract original product key.",
            "ja-JP" => "WinPEまたはデュアルブート環境で、起動しないパーティション（例: D:\\Windows）を指定してキーを直接抽出します。",
            _ => "在 WinPE 或雙系統下，指定無法開機的副磁區路徑（如 D:\\Windows），直接解碼提取原系統金鑰。"
        };
        OfflineWindowsPathBox.Header = lang switch { "zh-CN" => "离线系统路径", "en-US" => "Offline Windows Path", "ja-JP" => "オフライン Windows パス", _ => "離線 Windows 路徑 (Offline Windows Path)" };
        ReadOfflineKeyBtn.Content = lang switch { "zh-CN" => "🔍 提取离线密钥", "en-US" => "🔍 Extract Offline Key", "ja-JP" => "🔍 オフラインキーを抽出", _ => "🔍 提取離線金鑰" };
        ExtractedOfflineKeyBox.Header = lang switch { "zh-CN" => "提取的离线密钥", "en-US" => "Extracted Offline Key", "ja-JP" => "抽出されたキー", _ => "提取的離線金鑰 (Extracted Offline Key)" };

        // Tab 2: Environment Doctor
        EnvDoctorDesc.Text = lang switch
        {
            "zh-CN" => "自动检测本机是否具备各项高级磁盘与修复工具。若有缺失，可于应用内一键后台下载安装：",
            "en-US" => "Automatically detects disk and repair tools on this system. Missing tools can be installed directly in background:",
            "ja-JP" => "システム上の高度なディスク修復ツールの有無を診断します。不足しているツールはバックグラウンドで一括インストール可能：",
            _ => "自動偵測本機是否具備各項進階磁碟與修復工具。若有缺失，可於應用內一鍵背景下載安裝："
        };

        // Tab 3: Advanced Maintenance & Recovery
        SsdTrimCardTitle.Text = lang switch { "zh-CN" => "SSD TRIM 与磁盘优化", "en-US" => "SSD TRIM & Disk Optimization", "ja-JP" => "SSD TRIM とディスク最適化", _ => "SSD TRIM & 磁碟最佳化" };
        OptimizeDriveBox.Header = lang switch { "zh-CN" => "盘符", "en-US" => "Drive Letter", "ja-JP" => "ドライブ文字", _ => "磁碟機代號 (Drive Letter)" };
        RunTrimBtn.Content = lang switch { "zh-CN" => "⚡ SSD TRIM", "en-US" => "⚡ SSD TRIM", "ja-JP" => "⚡ SSD TRIM", _ => "⚡ SSD TRIM" };
        AnalyzeDefragBtn.Content = lang switch { "zh-CN" => "📊 碎片分析", "en-US" => "📊 Analyze", "ja-JP" => "📊 断片化分析", _ => "📊 碎片分析" };
        DefragHddBtn.Content = lang switch { "zh-CN" => "🧹 HDD 碎片整理", "en-US" => "🧹 Defrag HDD", "ja-JP" => "🧹 HDD デフラグ", _ => "🧹 HDD 重組" };
        OptimizeSmartBtn.Content = lang switch { "zh-CN" => "🚀 智能优化", "en-US" => "🚀 Smart Optimize", "ja-JP" => "🚀 スマート最適化", _ => "🚀 智慧最佳化" };
        ConsolidateFreeBtn.Content = lang switch { "zh-CN" => "🧱 合并可用空间 (/X)", "en-US" => "🧱 Consolidate Free Space (/X)", "ja-JP" => "🧱 空き領域の統合 (/X)", _ => "🧱 合併可用空間 (/X)" };
        SsdTrimSupportLabel.Text = lang switch { "zh-CN" => "SSD TRIM 支持", "en-US" => "SSD TRIM Support", "ja-JP" => "SSD TRIM サポート", _ => "SSD TRIM 支援" };
        SsdTrimSupportDesc.Text = lang switch { "zh-CN" => "保持闪存写入寿命与垃圾回收性能", "en-US" => "Preserves NAND write endurance and GC throughput", "ja-JP" => "フラッシュ書き込み寿命とガベージコレクション性能を維持", _ => "保持快閃記憶體寫入壽命與垃圾回收效能" };

        SafeEraseCardTitle.Text = lang switch { "zh-CN" => "安全擦除与卷影副本", "en-US" => "Secure Wipe & Volume Shadow Copies", "ja-JP" => "安全消去とシャドウコピー", _ => "安全抹除與陰影複本" };
        CipherDescText.Text = lang switch { "zh-CN" => "cipher.exe: 3 遍完整覆盖可用空间，防止已删除文件还原", "en-US" => "cipher.exe: 3-pass overwrite of free space to prevent recovery", "ja-JP" => "cipher.exe: 空き領域を3回完全上書きし、削除済みデータの復元を防止", _ => "cipher.exe: 3 遍完整覆寫可用空間，防已刪檔案還原" };
        RunCipherBtn.Content = lang switch { "zh-CN" => "🛡️ 安全擦除已删文件 (cipher /w)", "en-US" => "🛡️ Secure Wipe Free Space (cipher /w)", "ja-JP" => "🛡️ 削除データを安全消去 (cipher /w)", _ => "🛡️ 安全抹除已刪檔案 (cipher /w)" };
        VssTitleText.Text = lang switch { "zh-CN" => "vssadmin: 卷影副本管理", "en-US" => "vssadmin: Volume Shadow Copies", "ja-JP" => "vssadmin: ボリュームシャドウコピー管理", _ => "vssadmin: 磁碟區陰影複本 (Volume Shadow Copies)" };
        ListShadowsBtn.Content = lang switch { "zh-CN" => "📋 列出副本 (UI 清单)", "en-US" => "📋 List Shadows", "ja-JP" => "📋 シャドウコピー一覧", _ => "📋 列出複本 (UI 清單)" };
        DeleteAllShadowsBtn.Content = lang switch { "zh-CN" => "🗑️ 清除所有副本", "en-US" => "🗑️ Delete All Shadows", "ja-JP" => "🗑️ すべてのシャドウコピーを削除", _ => "🗑️ 清除所有複本" };
        RestorePointsTitleText.Text = lang switch { "zh-CN" => "系统还原点", "en-US" => "System Restore Points", "ja-JP" => "システム復元ポイント", _ => "System Restore: 系統還原點 (Restore Points)" };
        ListRestorePointsBtn.Content = lang switch { "zh-CN" => "📸 列出还原点", "en-US" => "📸 List Restore Points", "ja-JP" => "📸 復元ポイント一覧", _ => "📸 列出還原點" };
        CreateRestorePointBtn.Content = lang switch { "zh-CN" => "➕ 创建还原点", "en-US" => "➕ Create Restore Point", "ja-JP" => "➕ 復元ポイントを作成", _ => "➕ 建立還原點" };

        BitLockerCardTitle.Text = lang switch { "zh-CN" => "BitLocker 磁盘加密管理", "en-US" => "BitLocker Drive Encryption", "ja-JP" => "BitLocker ドライブ暗号化管理", _ => "BitLocker 磁碟加密管理" };
        BitLockerDriveBox.Header = lang switch { "zh-CN" => "盘符", "en-US" => "Drive", "ja-JP" => "ドライブ", _ => "磁區" };
        BitLockerPasswordBox.Header = lang switch { "zh-CN" => "恢复密码 (48位数字)", "en-US" => "Recovery Password (48 digits)", "ja-JP" => "回復パスワード (48桁)", _ => "修復密碼 (48位數字)" };
        BitLockerStatusBtn.Content = lang switch { "zh-CN" => "📋 加密状态", "en-US" => "📋 Status", "ja-JP" => "📋 暗号化状態", _ => "📋 加密狀態" };
        BitLockerProtectorsBtn.Content = lang switch { "zh-CN" => "🔑 密钥保护者", "en-US" => "🔑 Protectors", "ja-JP" => "🔑 キー保護機能", _ => "🔑 金鑰保護者" };
        BitLockerProtectLabel.Text = lang switch { "zh-CN" => "BitLocker 保护状态", "en-US" => "BitLocker Protection", "ja-JP" => "BitLocker 保護状態", _ => "BitLocker 保護狀態" };
        BitLockerProtectDesc.Text = lang switch { "zh-CN" => "暂停保护或恢复硬件层级加密", "en-US" => "Suspend or resume drive encryption protection", "ja-JP" => "暗号化保護の中断または再開", _ => "暫停保護或恢復硬體層級加密" };
        BitLockerLockBtn.Content = lang switch { "zh-CN" => "🔒 立即强制锁定", "en-US" => "🔒 Lock Drive", "ja-JP" => "🔒 強制ロック", _ => "🔒 立即強制鎖定" };
        BitLockerBackupKeyBtn.Content = lang switch { "zh-CN" => "🔑 备份恢复密钥", "en-US" => "🔑 Backup Key", "ja-JP" => "🔑 回復キーをバックアップ", _ => "🔑 備份修復金鑰" };
        BitLockerUnlockBtn.Content = lang switch { "zh-CN" => "🔓 解锁分区", "en-US" => "🔓 Unlock Drive", "ja-JP" => "🔓 ドライブのロック解除", _ => "🔓 解鎖磁區" };
        BitLockerDecryptBtn.Content = lang switch { "zh-CN" => "🔓 永久解密", "en-US" => "🔓 Decrypt Drive", "ja-JP" => "🔓 永久復号 (Decrypt)", _ => "🔓 永久解密 (Decrypt)" };

        WinFrCardTitle.Text = lang switch { "zh-CN" => "微软官方文件救援 (WinFR)", "en-US" => "Windows File Recovery (WinFR)", "ja-JP" => "公式ファイル救出 (WinFR)", _ => "微軟官方檔案救援 (WinFR)" };
        WinFrSourceBox.Header = lang switch { "zh-CN" => "源磁盘 (Source)", "en-US" => "Source Drive", "ja-JP" => "対象ドライブ (Source)", _ => "來源磁碟區 (Source Drive)" };
        WinFrDestBox.Header = lang switch { "zh-CN" => "保存目标文件夹 (Destination)", "en-US" => "Destination Folder", "ja-JP" => "保存先フォルダ (Destination)", _ => "救援存放資料夾 (Destination Folder)" };
        RunWinFrBtn.Content = lang switch { "zh-CN" => "🔍 开始扫描救回文件", "en-US" => "🔍 Start WinFR Recovery", "ja-JP" => "🔍 スキャンしてファイルを救出", _ => "🔍 開始掃描救回檔案" };

        VirtualDiskCardTitle.Text = lang switch { "zh-CN" => "挂载虚拟磁盘 (ISO/VHD)", "en-US" => "Mount Virtual Disk (ISO/VHD)", "ja-JP" => "仮想ディスクをマウント (ISO/VHD)", _ => "掛載虛擬磁碟 (ISO/VHD)" };
        VirtualDiskPathBox.Header = lang switch { "zh-CN" => "镜像文件路径", "en-US" => "Image File Path", "ja-JP" => "イメージファイルパス", _ => "映像檔路徑" };
        MountVirtualDiskBtn.Content = lang switch { "zh-CN" => "💿 一键挂载镜像", "en-US" => "💿 Mount Image", "ja-JP" => "💿 イメージをマウント", _ => "💿 一鍵掛載映像 (Mount)" };
        DismountVirtualDiskBtn.Content = lang switch { "zh-CN" => "⏏️ 卸载镜像", "en-US" => "⏏️ Dismount Image", "ja-JP" => "⏏️ イメージをアンマウント", _ => "⏏️ 卸載映像 (Dismount)" };

        Ext4CardTitle.Text = lang switch { "zh-CN" => "EXT4 Linux 分区与驱动", "en-US" => "WSL2 EXT4 & OEM Drivers", "ja-JP" => "EXT4 Linux ドライブとドライバ", _ => "EXT4 Linux 磁區 & 驅動" };
        Ext4DiskBox.Header = lang switch { "zh-CN" => "物理磁盘编号 (Disk #)", "en-US" => "Physical Disk #", "ja-JP" => "物理ディスク番号 (Disk #)", _ => "實體磁碟編號 (Disk #)" };
        MountExt4Btn.Content = lang switch { "zh-CN" => "🐧 挂载 EXT4", "en-US" => "🐧 Mount EXT4", "ja-JP" => "🐧 EXT4 をマウント", _ => "🐧 掛載 EXT4" };
        UnmountExt4Btn.Content = lang switch { "zh-CN" => "⏏️ 卸载 EXT4", "en-US" => "⏏️ Unmount EXT4", "ja-JP" => "⏏️ EXT4 をアンマウント", _ => "⏏️ 卸載 EXT4" };
        InstallDriverBtn.Content = lang switch { "zh-CN" => "➕ 安装驱动程序 (.inf)", "en-US" => "➕ Install Driver (.inf)", "ja-JP" => "➕ ドライバをインストール (.inf)", _ => "➕ 安裝/注入驅動程式 (.inf)" };
        LoadDriversBtn.Content = lang switch { "zh-CN" => "📋 枚举驱动清单", "en-US" => "📋 List OEM Drivers", "ja-JP" => "📋 ドライバ一覧を取得", _ => "📋 列舉驅動 (UI 清單)" };
        ExportDriversBtn.Content = lang switch { "zh-CN" => "💾 导出驱动", "en-US" => "💾 Export Drivers", "ja-JP" => "💾 ドライバをエクスポート", _ => "💾 匯出驅動" };
        DriverFilterBox.PlaceholderText = lang switch { "zh-CN" => "🔍 搜索驱动 (OEM/Class/Provider)...", "en-US" => "🔍 Search drivers (OEM/Class/Provider)...", "ja-JP" => "🔍 ドライバを検索 (OEM/Class/Provider)...", _ => "🔍 搜尋驅動 (OEM/Class/Provider)..." };

        ActionLogTitle.Text = lang switch { "zh-CN" => "执行日志", "en-US" => "Action Output Log", "ja-JP" => "実行ログ", _ => "執行日誌 (Action Output Log)" };
    }

    private bool _suppressToggled = false;

    private void RestoreCardOrder()
    {
        try
        {
            var order = SettingsService.Instance.Current.DiskHealthCardOrder;
            if (order != null && order.Count > 0)
            {
                var elements = HealthCardsStack.Children.OfType<FrameworkElement>().ToList();
                HealthCardsStack.Children.Clear();
                foreach (var tag in order)
                {
                    var found = elements.FirstOrDefault(e => (e.Tag as string) == tag || e.Name == tag);
                    if (found != null)
                    {
                        HealthCardsStack.Children.Add(found);
                        elements.Remove(found);
                    }
                }
                foreach (var remaining in elements)
                {
                    HealthCardsStack.Children.Add(remaining);
                }
            }
        }
        catch { }
    }

    private void SaveCardOrder()
    {
        try
        {
            var order = HealthCardsStack.Children
                .OfType<FrameworkElement>()
                .Select(e => (e.Tag as string) ?? e.Name)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
            var s = SettingsService.Instance.Current;
            s.DiskHealthCardOrder = order;
            SettingsService.Instance.Save();
        }
        catch { }
    }

    private void MoveCard(FrameworkElement card, int direction)
    {
        int index = HealthCardsStack.Children.IndexOf(card);
        if (index < 0) return;
        int targetIndex = index + direction;
        if (targetIndex >= 0 && targetIndex < HealthCardsStack.Children.Count)
        {
            HealthCardsStack.Children.RemoveAt(index);
            HealthCardsStack.Children.Insert(targetIndex, card);
            SaveCardOrder();
        }
    }

    private void MoveCardToExtreme(FrameworkElement card, bool toTop)
    {
        int index = HealthCardsStack.Children.IndexOf(card);
        if (index < 0) return;
        HealthCardsStack.Children.RemoveAt(index);
        if (toTop)
            HealthCardsStack.Children.Insert(0, card);
        else
            HealthCardsStack.Children.Add(card);
        SaveCardOrder();
    }

    private void CardOverview_MoveUp_Click(object sender, RoutedEventArgs e) => MoveCard(SmartOverviewCard, -1);
    private void CardOverview_MoveDown_Click(object sender, RoutedEventArgs e) => MoveCard(SmartOverviewCard, 1);
    private void CardOverview_MoveTop_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(SmartOverviewCard, true);
    private void CardOverview_MoveBottom_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(SmartOverviewCard, false);

    private void CardTelemetry_MoveUp_Click(object sender, RoutedEventArgs e) => MoveCard(SmartTelemetryCard, -1);
    private void CardTelemetry_MoveDown_Click(object sender, RoutedEventArgs e) => MoveCard(SmartTelemetryCard, 1);
    private void CardTelemetry_MoveTop_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(SmartTelemetryCard, true);
    private void CardTelemetry_MoveBottom_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(SmartTelemetryCard, false);

    private void CardReliability_MoveUp_Click(object sender, RoutedEventArgs e) => MoveCard(ReliabilityExpander, -1);
    private void CardReliability_MoveDown_Click(object sender, RoutedEventArgs e) => MoveCard(ReliabilityExpander, 1);
    private void CardReliability_MoveTop_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(ReliabilityExpander, true);
    private void CardReliability_MoveBottom_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(ReliabilityExpander, false);

    private void CardChkdsk_MoveUp_Click(object sender, RoutedEventArgs e) => MoveCard(ChkdskCard, -1);
    private void CardChkdsk_MoveDown_Click(object sender, RoutedEventArgs e) => MoveCard(ChkdskCard, 1);
    private void CardChkdsk_MoveTop_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(ChkdskCard, true);
    private void CardChkdsk_MoveBottom_Click(object sender, RoutedEventArgs e) => MoveCardToExtreme(ChkdskCard, false);

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _suppressToggled = true;
        try
        {
            ApplyLanguage();
            RestoreCardOrder();
            await ViewModel.RefreshAllCommand.ExecuteAsync(null);
        }
        finally
        {
            _suppressToggled = false;
        }
    }

    private async void DiskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView lv && lv.SelectedItem is Models.DiskHealthInfo disk)
        {
            await ViewModel.SelectDiskForSmartCommand.ExecuteAsync(disk);
        }
    }

    private void TempUnit_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && sender is ToggleSwitch sw)
        {
            ViewModel.ToggleTempUnitCommand.Execute(sw.IsOn);
        }
    }

    private void RawHex_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_suppressToggled && sender is ToggleSwitch sw)
        {
            ViewModel.ToggleRawHexCommand.Execute(sw.IsOn);
        }
    }

    private async void Trim_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsTrimEnabled)
        {
            await ViewModel.ToggleTrimCommand.ExecuteAsync(sw.IsOn);
        }
    }

    private async void BitLockerProtect_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggled || ViewModel.IsUpdatingProgrammatically) return;
        if (sender is ToggleSwitch sw && sw.IsOn != ViewModel.IsBitLockerActive)
        {
            await ViewModel.ToggleBitLockerProtectionCommand.ExecuteAsync(sw.IsOn);
        }
    }

    private void InstallTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Services.ToolEnvironmentItem item)
        {
            ViewModel.InstallToolCommand.Execute(item);
        }
    }

    private void DeleteShadow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Models.VssShadowItem item)
        {
            ViewModel.DeleteShadowDirectCommand.Execute(item);
        }
    }

    private void DeleteDriver_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Models.OemDriverItem item)
        {
            ViewModel.DeleteDriverDirectCommand.Execute(item);
        }
    }
}
