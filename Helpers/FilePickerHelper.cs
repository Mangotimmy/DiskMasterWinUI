using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DiskMasterWinUI.Helpers;

public static class FilePickerHelper
{
    private const int MAX_PATH_CHARS = 4096;

    // ── Win32 comdlg32.dll (GetOpenFileNameW / GetSaveFileNameW) ──
    private const int OFN_READONLY = 0x00000001;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_HIDEREADONLY = 0x00000004;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_DONTADDTORECENT = 0x02000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("comdlg32.dll")]
    private static extern int CommDlgExtendedError();

    // ── Win32 shell32.dll (SHBrowseForFolderW fallback) ──
    private const uint BIF_RETURNONLYFSDIRS = 0x00000001;
    private const uint BIF_NEWDIALOGSTYLE = 0x00000040;
    private const uint BIF_EDITBOX = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFOW
    {
        public nint hwndOwner;
        public nint pidlRoot;
        public nint pszDisplayName;
        public string? lpszTitle;
        public uint ulFlags;
        public nint lpfn;
        public nint lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHBrowseForFolderW(ref BROWSEINFOW lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(nint pidl, nint pszPath);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(nint pv);

    // ── COM IFileOpenDialog for modern Folder Picker (works in elevated/unpackaged mode) ──
    private const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

    [Flags]
    private enum FOS : uint
    {
        FOS_OVERWRITEPROMPT = 0x00000002,
        FOS_NOCHANGEDIR = 0x00000008,
        FOS_PICKFOLDERS = 0x00000020,
        FOS_FORCEFILESYSTEM = 0x00000040,
        FOS_PATHMUSTEXIST = 0x00000800,
        FOS_FILEMUSTEXIST = 0x00001000,
        FOS_DONTADDTORECENT = 0x02000000
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport]
    [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(nint parent);
        void SetFileTypes(uint cFileTypes, nint rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(nint pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint pbc, [In] ref Guid bhid, [In] ref Guid riid, out nint ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    /// <summary>
    /// Picks a single file using native Win32 GetOpenFileNameW on an STA thread
    /// (works reliably in unpackaged WinUI 3 even when running as Administrator and supports multi-dot extensions),
    /// with WinRT FileOpenPicker and PowerShell STA fallbacks.
    /// </summary>
    public static async Task<string?> PickSingleFileAsync(string[]? extensions = null, string? title = null)
    {
        var exts = (extensions == null || extensions.Length == 0) ? ["*"] : extensions;
        var hwnd = WindowHelper.GetWindowHandle();

        // 1. Primary: Native Win32 GetOpenFileNameW on a dedicated STA thread
        var (win32Success, win32Path) = await RunOnStaThreadAsync(() => TryWin32OpenFile(hwnd, exts, title));
        if (win32Success)
        {
            return win32Path; // Either chosen path or null (if user cancelled)
        }

        // 2. Secondary: WinRT FileOpenPicker (with sanitized single-dot extensions)
        try
        {
            var picker = new FileOpenPicker();
            if (hwnd != 0)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }

            var addedAny = false;
            foreach (var rawExt in exts)
            {
                if (string.IsNullOrWhiteSpace(rawExt)) continue;
                var trimmed = rawExt.Trim();
                if (trimmed is "*" or "*.*")
                {
                    picker.FileTypeFilter.Add("*");
                    addedAny = true;
                    continue;
                }

                // WinRT FileOpenPicker rejects multi-dot extensions like ".model3.json"
                var lastDot = trimmed.LastIndexOf('.');
                var singleExt = lastDot >= 0 ? trimmed[lastDot..] : "." + trimmed;
                if (singleExt.Length > 1 && !picker.FileTypeFilter.Contains(singleExt))
                {
                    picker.FileTypeFilter.Add(singleExt);
                    addedAny = true;
                }
            }

            if (!addedAny)
            {
                picker.FileTypeFilter.Add("*");
            }

            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            // 3. Tertiary: PowerShell -Sta OpenFileDialog fallback
            return await FallbackPickFilePowerShellAsync(exts);
        }
    }

    /// <summary>
    /// Picks a save file destination using native Win32 GetSaveFileNameW on an STA thread,
    /// with WinRT FileSavePicker fallback.
    /// </summary>
    public static async Task<string?> PickSaveFileAsync(
        string suggestedFileName,
        string defaultExtension = ".bcd",
        string filterDescription = "Backup File",
        string? title = null)
    {
        var hwnd = WindowHelper.GetWindowHandle();
        var cleanExt = defaultExtension.StartsWith('.') ? defaultExtension : "." + defaultExtension;

        var (win32Success, win32Path) = await RunOnStaThreadAsync(() =>
            TryWin32SaveFile(hwnd, suggestedFileName, cleanExt, filterDescription, title));
        if (win32Success)
        {
            return win32Path;
        }

        try
        {
            var picker = new FileSavePicker();
            if (hwnd != 0)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add(filterDescription, new List<string> { cleanExt });
            picker.SuggestedFileName = suggestedFileName;

            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Picks a folder using COM IFileOpenDialog (FOS_PICKFOLDERS) on an STA thread
    /// (works reliably when elevated as Administrator in unpackaged WinUI 3),
    /// falling back to SHBrowseForFolderW and PowerShell -Sta FolderBrowserDialog.
    /// </summary>
    public static async Task<string?> PickFolderAsync(string? title = null)
    {
        var hwnd = WindowHelper.GetWindowHandle();

        // 1. Primary: Modern COM IFileOpenDialog with FOS_PICKFOLDERS on STA thread
        var (comHandled, comPath) = await RunOnStaThreadAsync(() => TryComPickFolder(hwnd, title));
        if (comHandled)
        {
            return comPath; // Either chosen folder or null if user cancelled
        }

        // 2. Secondary: WinRT FolderPicker
        try
        {
            var picker = new FolderPicker();
            if (hwnd != 0)
            {
                InitializeWithWindow.Initialize(picker, hwnd);
            }
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) return folder.Path;
            return null;
        }
        catch
        {
            // 3. Tertiary: Win32 SHBrowseForFolderW on STA thread, then PowerShell -Sta
            var (shHandled, shPath) = await RunOnStaThreadAsync(() => TryShBrowseFolder(hwnd, title));
            if (shHandled) return shPath;

            return await FallbackPickFolderPowerShellAsync();
        }
    }

    private static (bool Handled, string? Path) TryWin32OpenFile(nint hwnd, string[] extensions, string? title)
    {
        nint fileBuffer = nint.Zero;
        try
        {
            var patterns = extensions
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e =>
                {
                    var t = e.Trim();
                    if (t is "*" or "*.*") return "*.*";
                    return t.StartsWith("*.") ? t : (t.StartsWith('.') ? "*" + t : "*." + t);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (patterns.Length == 0) patterns = ["*.*"];
            var joinedPatterns = string.Join(";", patterns);
            var filter = $"Supported Files ({joinedPatterns})\0{joinedPatterns}\0All Files (*.*)\0*.*\0\0";

            fileBuffer = Marshal.AllocHGlobal(MAX_PATH_CHARS * 2);
            Marshal.WriteInt16(fileBuffer, 0, 0);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = hwnd,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MAX_PATH_CHARS,
                lpstrTitle = title,
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_DONTADDTORECENT
            };

            if (GetOpenFileNameW(ref ofn))
            {
                var selected = Marshal.PtrToStringUni(fileBuffer);
                return (true, string.IsNullOrWhiteSpace(selected) ? null : selected);
            }

            // If CommDlgExtendedError() == 0, the user simply clicked Cancel/Close.
            var err = CommDlgExtendedError();
            return err == 0 ? (true, null) : (false, null);
        }
        catch
        {
            return (false, null);
        }
        finally
        {
            if (fileBuffer != nint.Zero) Marshal.FreeHGlobal(fileBuffer);
        }
    }

    private static (bool Handled, string? Path) TryWin32SaveFile(
        nint hwnd,
        string suggestedFileName,
        string cleanExt,
        string filterDescription,
        string? title)
    {
        nint fileBuffer = nint.Zero;
        try
        {
            var pattern = "*" + cleanExt;
            var filter = $"{filterDescription} ({pattern})\0{pattern}\0All Files (*.*)\0*.*\0\0";

            fileBuffer = Marshal.AllocHGlobal(MAX_PATH_CHARS * 2);
            var chars = (suggestedFileName ?? "").ToCharArray();
            Marshal.Copy(chars, 0, fileBuffer, Math.Min(chars.Length, MAX_PATH_CHARS - 1));
            Marshal.WriteInt16(fileBuffer, Math.Min(chars.Length, MAX_PATH_CHARS - 1) * 2, 0);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = hwnd,
                lpstrFilter = filter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MAX_PATH_CHARS,
                lpstrTitle = title,
                lpstrDefExt = cleanExt.TrimStart('.'),
                Flags = OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_DONTADDTORECENT
            };

            if (GetSaveFileNameW(ref ofn))
            {
                var selected = Marshal.PtrToStringUni(fileBuffer);
                return (true, string.IsNullOrWhiteSpace(selected) ? null : selected);
            }

            var err = CommDlgExtendedError();
            return err == 0 ? (true, null) : (false, null);
        }
        catch
        {
            return (false, null);
        }
        finally
        {
            if (fileBuffer != nint.Zero) Marshal.FreeHGlobal(fileBuffer);
        }
    }

    private static (bool Handled, string? Path) TryComPickFolder(nint hwnd, string? title)
    {
        IFileOpenDialog? dialog = null;
        IShellItem? item = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PATHMUSTEXIST | FOS.FOS_NOCHANGEDIR | FOS.FOS_DONTADDTORECENT);
            if (!string.IsNullOrWhiteSpace(title))
            {
                dialog.SetTitle(title);
            }

            var hr = dialog.Show(hwnd);
            if (hr == ERROR_CANCELLED_HR)
            {
                return (true, null); // User cancelled
            }
            if (hr < 0)
            {
                return (false, null);
            }

            dialog.GetResult(out item);
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
            return (true, string.IsNullOrWhiteSpace(path) ? null : path);
        }
        catch (COMException comEx) when (comEx.HResult == ERROR_CANCELLED_HR)
        {
            return (true, null);
        }
        catch
        {
            return (false, null);
        }
        finally
        {
            if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            if (dialog != null && Marshal.IsComObject(dialog)) Marshal.ReleaseComObject(dialog);
        }
    }

    private static (bool Handled, string? Path) TryShBrowseFolder(nint hwnd, string? title)
    {
        nint displayBuffer = nint.Zero;
        nint pathBuffer = nint.Zero;
        nint pidl = nint.Zero;
        try
        {
            displayBuffer = Marshal.AllocHGlobal(MAX_PATH_CHARS * 2);
            pathBuffer = Marshal.AllocHGlobal(MAX_PATH_CHARS * 2);
            Marshal.WriteInt16(displayBuffer, 0, 0);
            Marshal.WriteInt16(pathBuffer, 0, 0);

            var bi = new BROWSEINFOW
            {
                hwndOwner = hwnd,
                pszDisplayName = displayBuffer,
                lpszTitle = title ?? "選擇資料夾 (Select Folder)",
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX
            };

            pidl = SHBrowseForFolderW(ref bi);
            if (pidl == nint.Zero)
            {
                return (true, null);
            }

            if (SHGetPathFromIDListW(pidl, pathBuffer))
            {
                var path = Marshal.PtrToStringUni(pathBuffer);
                return (true, string.IsNullOrWhiteSpace(path) ? null : path);
            }

            return (false, null);
        }
        catch
        {
            return (false, null);
        }
        finally
        {
            if (pidl != nint.Zero) CoTaskMemFree(pidl);
            if (displayBuffer != nint.Zero) Marshal.FreeHGlobal(displayBuffer);
            if (pathBuffer != nint.Zero) Marshal.FreeHGlobal(pathBuffer);
        }
    }

    private static Task<T> RunOnStaThreadAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "DiskMaster-FilePicker-STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static async Task<string?> FallbackPickFilePowerShellAsync(string[] extensions)
    {
        try
        {
            var filter = string.Join(";", extensions.Select(e =>
            {
                var t = e.Trim();
                if (t is "*" or "*.*") return "*.*";
                return t.StartsWith("*.") ? t : (t.StartsWith('.') ? "*" + t : "*." + t);
            }));
            var psScript = $@"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.OpenFileDialog; $f.Filter = 'Files ({filter})|{filter}|All Files (*.*)|*.*'; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ $f.FileName }}";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Sta -Command \"{psScript}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var res = (await p.StandardOutput.ReadToEndAsync()).Trim();
                await p.WaitForExitAsync();
                return string.IsNullOrWhiteSpace(res) ? null : res;
            }
        }
        catch { }
        return null;
    }

    private static async Task<string?> FallbackPickFolderPowerShellAsync()
    {
        try
        {
            var psScript = @"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $f.SelectedPath }";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Sta -Command \"{psScript}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var res = (await p.StandardOutput.ReadToEndAsync()).Trim();
                await p.WaitForExitAsync();
                return string.IsNullOrWhiteSpace(res) ? null : res;
            }
        }
        catch { }
        return null;
    }
}
