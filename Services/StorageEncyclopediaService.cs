using System.Diagnostics;
using System.IO;
using DiskMasterWinUI.Helpers;
using DiskMasterWinUI.Models;

namespace DiskMasterWinUI.Services;

public class StorageEncyclopediaService
{
    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SystemDrive = Path.GetPathRoot(WinDir) ?? @"C:\";
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string UserTemp = Path.GetTempPath();

    public List<StorageDirectoryItem> GetDefaultCatalog()
    {
        return new List<StorageDirectoryItem>
        {
            new()
            {
                Id = "WinSxS",
                Path = Path.Combine(WinDir, "WinSxS"),
                NameZhTw = "WinSxS (Windows 元件存放區)",
                NameZhCn = "WinSxS (Windows 组件存储区)",
                NameEnUs = "WinSxS (Windows Component Store)",
                NameJaJp = "WinSxS (コンポーネント ストア)",
                DescZhTw = "存放系統所有 Windows 功能、更新備份與硬連結。切勿直接透過檔案總管強制刪除檔案，否則會導致系統損毀；應使用 DISM /Cleanup-Image 進行安全深層瘦身與版本合併。",
                DescZhCn = "存储系统所有 Windows 功能、更新备份与硬链接。切勿直接通过文件管理器强制删除文件，否则会导致系统损坏；应使用 DISM /Cleanup-Image 进行安全深层瘦身与版本合并。",
                DescEnUs = "Stores Windows component packages, update backups, and hardlinks. Never delete files directly via Explorer as it causes OS corruption; safely reclaim space using DISM /Cleanup-Image.",
                DescJaJp = "Windows の更新プログラムやコンポーネント、ハードリンクを保持します。手動で強制削除するとシステムが破損するため、DISM /Cleanup-Image による安全な整理が必須です。",
                SafetyLevel = StorageSafetyLevel.CleanViaTool,
                ActionType = "DismClean",
                Icon = "🧩"
            },
            new()
            {
                Id = "SoftwareDistribution",
                Path = Path.Combine(WinDir, "SoftwareDistribution"),
                NameZhTw = "SoftwareDistribution (Windows Update 快取)",
                NameZhCn = "SoftwareDistribution (Windows Update 缓存)",
                NameEnUs = "SoftwareDistribution (Windows Update Cache)",
                NameJaJp = "SoftwareDistribution (更新プログラム キャッシュ)",
                DescZhTw = "存放 Windows Update 已下載的修補套件、暫存資料與更新歷史資料庫。若更新發生錯誤或體積異常肥大，可透過 DiskMaster 一鍵重置 SoftwareDistribution 資料夾釋放數 GB 空間。",
                DescZhCn = "存储 Windows Update 已下载的补丁包、暂存数据与更新历史数据库。若更新出现报错或体积异常肥大，可通过 DiskMaster 一键重构 SoftwareDistribution 文件夹释放数 GB 空间。",
                DescEnUs = "Stores Windows Update downloaded patches, staging data, and datastore history. Safe to purge and reset if Windows Update encounters errors or consumes excess SSD space.",
                DescJaJp = "Windows Update のダウンロード済みパッチと履歴データベースです。更新エラー発生時や大容量消費時に再構築・安全クリーンアップが可能です。",
                SafetyLevel = StorageSafetyLevel.SafeToClean,
                ActionType = "SafeClean",
                Icon = "🔄"
            },
            new()
            {
                Id = "Installer",
                Path = Path.Combine(WinDir, "Installer"),
                NameZhTw = "Windows\\Installer (MSI 安裝套件快取)",
                NameZhCn = "Windows\\Installer (MSI 安装包缓存)",
                NameEnUs = "Windows\\Installer (MSI/MSP Cache)",
                NameJaJp = "Windows\\Installer (インストーラー キャッシュ)",
                DescZhTw = "存放軟體安裝時的 MSI 與 MSP 修補程式快取，提供日後反安裝或修復軟體使用。隨意清空會導致控制台無法解除安裝軟體。建議保留或僅清理無效的孤兒孤立套件。",
                DescZhCn = "存储软件安装时的 MSI 与 MSP 补丁缓存，供日后卸载或修复软件使用。随意清空会导致控制面板无法卸载已装软件。建议保留或仅清理无效的孤儿孤立安装包。",
                DescEnUs = "Contains cached .msi and .msp installers required for application maintenance and uninstallation. Arbitrary deletion breaks software uninstallers; keep intact.",
                DescJaJp = "アプリのアンインストールや修復に必要な MSI/MSP キャッシュです。直接削除するとアプリの削除ができなくなるため保持を推奨します。",
                SafetyLevel = StorageSafetyLevel.SystemCore,
                ActionType = "OpenExplorer",
                Icon = "📦"
            },
            new()
            {
                Id = "DriverStore",
                Path = Path.Combine(WinDir, "System32", "DriverStore", "FileRepository"),
                NameZhTw = "DriverStore\\FileRepository (驅動程式封存庫)",
                NameZhCn = "DriverStore\\FileRepository (驱动程序暂存仓库)",
                NameEnUs = "DriverStore\\FileRepository (Staged Driver Store)",
                NameJaJp = "DriverStore\\FileRepository (ドライバ リポジトリ)",
                DescZhTw = "Windows 隨附與第三方硬體驅動程式的備份封存庫。舊版顯卡或網卡驅動常年累積可達十幾 GB。請使用 DiskMaster 的「OEM 驅動管理」一鍵分析並卸載舊版驅動。",
                DescZhCn = "Windows 内置与第三方硬件驱动的备份暂存库。旧版显卡或网卡驱动常年累积可达数十 GB。请使用 DiskMaster 的「OEM 驱动管理」一键分析并卸载冗余旧版驱动。",
                DescEnUs = "Stores inbox and third-party driver packages. Legacy GPU/chipset drivers accumulate over time; use DiskMaster OEM Driver Manager to enumerate and uninstall old drivers cleanly.",
                DescJaJp = "サードパーティ製および組み込みドライバの格納庫です。旧バージョンが肥大化しやすいため、DiskMaster のドライバ管理機能で古いドライバを削除できます。",
                SafetyLevel = StorageSafetyLevel.CleanViaTool,
                ActionType = "OpenExplorer",
                Icon = "⚙️"
            },
            new()
            {
                Id = "SystemTemp",
                Path = Path.Combine(WinDir, "Temp"),
                NameZhTw = "Windows\\Temp (系統級暫存目錄)",
                NameZhCn = "Windows\\Temp (系统级临时文件夹)",
                NameEnUs = "Windows\\Temp (System Temp Directory)",
                NameJaJp = "Windows\\Temp (システム一時フォルダ)",
                DescZhTw = "Windows 系統服務、驅動安裝程式與後台作業所建立的暫存工作目錄。所有檔案均為可隨時安全刪除的暫存檔案，清理後可立即釋放磁碟空間。",
                DescZhCn = "Windows 系统服务、驱动安装程序与后台任务创建的临时工作目录。所有文件均可随时安全删除，清理后可立即释放磁盘空间。",
                DescEnUs = "Temporary files created by Windows system services and background installers. Completely safe to purge at any time.",
                DescJaJp = "システムサービスやインストーラーが使用する一時ディレクトリです。いつでも安全に完全消去可能です。",
                SafetyLevel = StorageSafetyLevel.SafeToClean,
                ActionType = "SafeClean",
                Icon = "🪟"
            },
            new()
            {
                Id = "UserTemp",
                Path = UserTemp,
                NameZhTw = "使用者暫存目錄 (%TEMP%)",
                NameZhCn = "用户临时目录 (%TEMP%)",
                NameEnUs = "User Temp Directory (%TEMP%)",
                NameJaJp = "ユーザー一時フォルダ (%TEMP%)",
                DescZhTw = "目前登入使用者所執行的瀏覽器、解壓縮工具與應用程式所產生的暫存垃圾與工作檔案。可百分之百安全一鍵全部清空。",
                DescZhCn = "当前登录用户所运行的浏览器、解压缩工具与应用程序产生的临时垃圾与工作文件。可百分之百安全一键全部清空。",
                DescEnUs = "User-level temporary files generated by browsers, archive utilities, and applications. 100% safe to purge.",
                DescJaJp = "ユーザーが実行したアプリ、ブラウザ、解凍ツールの一時ファイルです。安全に一括削除できます。",
                SafetyLevel = StorageSafetyLevel.SafeToClean,
                ActionType = "SafeClean",
                Icon = "👤"
            },
            new()
            {
                Id = "AppDataLocal",
                Path = LocalAppData,
                NameZhTw = "AppData\\Local (應用程式本機資料與快取)",
                NameZhCn = "AppData\\Local (应用程序本地数据与缓存)",
                NameEnUs = "AppData\\Local (App Local Data & Caches)",
                NameJaJp = "AppData\\Local (ローカル アプリ データ)",
                DescZhTw = "包含應用程式設定、本機資料庫、Chrome/Edge 快取與 Shader 著色器快取。切勿整個目錄刪除，否則軟體設定會遺失；建議透過專用清理清理內部快取子目錄。",
                DescZhCn = "包含应用程序设置、本地数据库、Chrome/Edge 缓存与着色器 Shader 缓存。切勿整个目录删除，否则软件配置会丢失；建议通过专用清理工具清理内部缓存子目录。",
                DescEnUs = "Stores user profile application settings, databases, browser caches, and GPU shader caches. Do not delete the root folder; clean only child cache folders.",
                DescJaJp = "ブラウザやアプリの設定、シェーダーキャッシュなどが格納されています。全体を削除せず、内部のキャッシュのみを対象にしてください。",
                SafetyLevel = StorageSafetyLevel.SystemCore,
                ActionType = "OpenExplorer",
                Icon = "💾"
            },
            new()
            {
                Id = "Hiberfil",
                Path = Path.Combine(SystemDrive, "hiberfil.sys"),
                NameZhTw = "hiberfil.sys (Windows 系統休眠檔案)",
                NameZhCn = "hiberfil.sys (Windows 系统休眠文件)",
                NameEnUs = "hiberfil.sys (System Hibernation File)",
                NameJaJp = "hiberfil.sys (ハイバネーション ファイル)",
                DescZhTw = "休眠與快速啟動（Fast Startup）所需的記憶體映射檔，體積約為實體 RAM 的 40%~100% (通常 8GB~32GB)。關閉休眠即可徹底刪除此檔案，釋放大容量 SSD 空間！",
                DescZhCn = "休眠与快速启动（Fast Startup）所需的内存映射文件，体积约为物理内存的 40%~100% (通常 8GB~32GB)。关闭休眠即可彻底删除该文件，释放大容量 SSD 空间！",
                DescEnUs = "Stores memory snapshot for hibernation and Fast Startup. Typically occupies 8GB to 32GB+ of SSD space. Disabling hibernation instantly purges this file.",
                DescJaJp = "休止状態と高速スタートアップ用のメモリ退避ファイルです。RAMの約40〜100%を占有します。休止を無効化することで即座に数十GBを解放できます。",
                SafetyLevel = StorageSafetyLevel.CleanViaTool,
                ActionType = "DisableHibernate",
                Icon = "⚡"
            },
            new()
            {
                Id = "Pagefile",
                Path = Path.Combine(SystemDrive, "pagefile.sys"),
                NameZhTw = "pagefile.sys (虛擬記憶體分頁檔案)",
                NameZhCn = "pagefile.sys (虚拟内存分页文件)",
                NameEnUs = "pagefile.sys (Virtual Memory Paging File)",
                NameJaJp = "pagefile.sys (仮想メモリ ページファイル)",
                DescZhTw = "Windows 當實體 RAM 不足時使用的硬碟虛擬記憶體分頁檔。系統核心動態鎖定保護中，不可手動刪除；可至「進階系統設定」調整分頁檔固定上限或移至其他磁碟。",
                DescZhCn = "Windows 当物理内存不足时使用的硬盘虚拟内存分页文件。系统内核动态锁定保护中，不可手动删除；可前往「高级系统设置」调整分页文件大小或迁移至其他磁盘。",
                DescEnUs = "Windows virtual memory paging file used to back physical RAM. Dynamically locked by Windows kernel; configure fixed sizing or migrate to secondary drive via Advanced System Settings.",
                DescJaJp = "物理メモリ不足時に使用される仮想メモリのページファイルです。カーネルによりロックされているため手動削除は不可です。",
                SafetyLevel = StorageSafetyLevel.SystemCore,
                ActionType = "OpenExplorer",
                Icon = "🧠"
            },
            new()
            {
                Id = "WindowsOld",
                Path = Path.Combine(SystemDrive, "Windows.old"),
                NameZhTw = "Windows.old (舊版系統回退備份目錄)",
                NameZhCn = "Windows.old (旧版系统回滚备份目录)",
                NameEnUs = "Windows.old (Previous Windows Installation)",
                NameJaJp = "Windows.old (旧 Windows バックアップ)",
                DescZhTw = "Windows 版本重大升級或覆蓋安裝後產生的舊系統回退備份，常佔用 20GB~50GB。若新系統運作穩定且無需退回舊版本，可安全徹底清除。",
                DescZhCn = "Windows 版本重大升级或覆盖安装后产生的旧系统回滚备份，常占用 20GB~50GB。若新系统运行稳定且无需回退，可安全彻底清除。",
                DescEnUs = "Backup of previous Windows installation generated during major feature updates (typically 20GB to 50GB). 100% safe to delete once system stability is verified.",
                DescJaJp = "Windowsの大型アップデート後に生成されるロールバック用バックアップです。動作が安定している場合は完全に安全に削除可能です。",
                SafetyLevel = StorageSafetyLevel.SafeToClean,
                ActionType = "SafeClean",
                Icon = "🕰️"
            },
            new()
            {
                Id = "SystemVolumeInfo",
                Path = Path.Combine(SystemDrive, "System Volume Information"),
                NameZhTw = "System Volume Information (陰影複製與還原點)",
                NameZhCn = "System Volume Information (卷影复制与还原点)",
                NameEnUs = "System Volume Information (VSS & Restore Points)",
                NameJaJp = "System Volume Information (VSS・復元ポイント)",
                DescZhTw = "儲存 NTFS 磁碟區陰影複製 (VSS Snapshot)、索引資料庫與 Windows 系統還原點。目錄受到 SYSTEM ACL 特權嚴格保護，可透過 DiskMaster 陰影複製管理進行精準瘦身。",
                DescZhCn = "存储 NTFS 卷影副本 (VSS 快照)、索引数据库与 Windows 系统还原点。目录受 SYSTEM ACL 特权严格保护，可通过 DiskMaster 卷影复制管理进行精准清理。",
                DescEnUs = "Stores NTFS volume shadow copies (VSS), search indexes, and system restore points. Protected by strict SYSTEM ACLs; prune safely via DiskMaster VSS Management.",
                DescJaJp = "ボリュームシャドウコピー (VSS) とシステムの復元ポイントを保持します。厳格なSYSTEM権限で保護されており、DiskMaster のVSS管理から安全に削除できます。",
                SafetyLevel = StorageSafetyLevel.SystemCore,
                ActionType = "OpenExplorer",
                Icon = "🛡️"
            }
        };
    }

    public async Task ScanDirectoryAsync(StorageDirectoryItem item, CancellationToken ct = default)
    {
        item.IsCalculating = true;
        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(item.Path))
                {
                    item.Exists = true;
                    try
                    {
                        var fi = new FileInfo(item.Path);
                        item.SizeBytes = fi.Length;
                        item.FileCount = 1;
                    }
                    catch
                    {
                        item.SizeBytes = 0;
                        item.FileCount = 1;
                    }
                    return;
                }

                if (!Directory.Exists(item.Path))
                {
                    item.Exists = false;
                    item.SizeBytes = 0;
                    item.FileCount = 0;
                    return;
                }

                item.Exists = true;
                long totalBytes = 0;
                int totalFiles = 0;

                CalculateFolderMetrics(item.Path, ref totalBytes, ref totalFiles, maxDepth: 4, currentDepth: 0, ct);

                item.SizeBytes = totalBytes;
                item.FileCount = totalFiles;
            }, ct);
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            item.IsCalculating = false;
            item.IsCalculated = true;
            item.NotifyLanguageChanged();
        }
    }

    private void CalculateFolderMetrics(string dirPath, ref long totalBytes, ref int totalFiles, int maxDepth, int currentDepth, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || currentDepth > maxDepth) return;

        try
        {
            var di = new DirectoryInfo(dirPath);
            foreach (var fi in di.EnumerateFiles())
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    totalBytes += fi.Length;
                    totalFiles++;
                }
                catch { }
            }

            foreach (var sub in di.EnumerateDirectories())
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    CalculateFolderMetrics(sub.FullName, ref totalBytes, ref totalFiles, maxDepth, currentDepth + 1, ct);
                }
                catch { }
            }
        }
        catch { }
    }

    public async Task<string> ExecuteActionAsync(StorageDirectoryItem item)
    {
        switch (item.ActionType)
        {
            case "OpenExplorer":
                try
                {
                    if (File.Exists(item.Path))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
                    }
                    else if (Directory.Exists(item.Path))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"") { UseShellExecute = true });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", SystemDrive) { UseShellExecute = true });
                    }
                    return "Opened in File Explorer.";
                }
                catch (Exception ex)
                {
                    return $"Failed to open Explorer: {ex.Message}";
                }

            case "SafeClean":
                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        int deletedFiles = 0;
                        long freedBytes = 0;
                        var di = new DirectoryInfo(item.Path);
                        foreach (var f in di.GetFiles("*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                long len = f.Length;
                                f.Delete();
                                freedBytes += len;
                                deletedFiles++;
                            }
                            catch { }
                        }
                        return $"Cleaned {deletedFiles} files, freed {freedBytes / (1024.0 * 1024.0):F1} MB.";
                    }
                    return "Directory does not exist.";
                }
                catch (Exception ex)
                {
                    return $"Cleanup error: {ex.Message}";
                }

            case "DismClean":
                return await RunDismComponentCleanupAsync();

            case "DisableHibernate":
                var pService = new PowerCfgService();
                var (success, msg) = await pService.SetHibernationAsync(false);
                return msg;

            default:
                return "Action not recognized.";
        }
    }

    public async Task<string> RunDismComponentCleanupAsync()
    {
        var (outStr, errStr, code) = await ProcessHelper.RunProcessAsync("dism.exe", "/online /cleanup-image /startcomponentcleanup");
        return code == 0 ? "DISM Component Store Cleanup successfully completed!" : (string.IsNullOrWhiteSpace(errStr) ? outStr : errStr);
    }

    public async Task<string> ExecuteSafeCleanupAsync(StorageDirectoryItem item)
    {
        return await ExecuteActionAsync(item);
    }
}
