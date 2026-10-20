using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.ViewModels;

public partial class ShareEditorViewModel : ObservableObject
{
    private readonly NetShareService _service = new();
    private readonly NetShareItem? _existing;

    [ObservableProperty] private string _shareName = "";
    [ObservableProperty] private string _folderPath = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double _concurrentUserLimit;
    [ObservableProperty] private string _cachingMode = "Manual";
    [ObservableProperty] private string _folderEnumerationMode = "Access-based";
    [ObservableProperty] private bool _encryptData;
    [ObservableProperty] private bool _applyNtfs;
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private string _validationMessage = "";
    [ObservableProperty] private bool _isEdit;

    public ObservableCollection<ShareAclGrantItem> Grants { get; } = new();
    public IReadOnlyList<string> AccessRights { get; } = new[] { "Read", "Change", "Full control" };
    public IReadOnlyList<string> CachingModes { get; } = new[] { "Manual", "Documents", "Programs", "No offline files", "BranchCache" };
    public IReadOnlyList<string> EnumerationModes { get; } = new[] { "Unrestricted", "Access-based", "Hide unspecified" };

    public bool IsCreate => !IsEdit;

    public ShareEditorViewModel(NetShareItem? existing = null)
    {
        _existing = existing;
        IsEdit = existing != null;
        if (existing != null)
        {
            ShareName = existing.ShareName;
            FolderPath = existing.ResourcePath;
            Description = existing.Remark;
            ConcurrentUserLimit = existing.ConcurrentUserLimit;
            CachingMode = NormalizeCachingDisplay(existing.CachingMode);
            FolderEnumerationMode = NormalizeEnumerationDisplay(existing.FolderEnumerationMode);
            EncryptData = existing.EncryptData;
        }
        else
        {
            Grants.Add(new ShareAclGrantItem { AccountName = "Authenticated Users", AccessRight = "Read" });
        }
    }

    public async Task InitializeAsync()
    {
        if (_existing == null) return;
        try
        {
            var details = await _service.GetShareDetailsAsync(_existing.ShareName);
            if (details != null)
            {
                Description = details.Description;
                ConcurrentUserLimit = details.ConcurrentUserLimit;
                CachingMode = NormalizeCachingDisplay(details.CachingMode);
                FolderEnumerationMode = NormalizeEnumerationDisplay(details.FolderEnumerationMode);
                EncryptData = details.EncryptData;
            }

            var acl = await _service.GetShareAclAsync(_existing.ShareName);
            Grants.Clear();
            foreach (var entry in acl)
            {
                Grants.Add(new ShareAclGrantItem
                {
                    AccountName = entry.Principal,
                    AccessRight = NormalizeAccess(entry.AccessRight)
                });
            }
        }
        catch (Exception ex)
        {
            ValidationMessage = ex.Message;
        }
    }

    public void AddGrant(string accountName, string accessRight)
    {
        if (string.IsNullOrWhiteSpace(accountName)) return;
        string account = accountName.Trim();
        string right = NormalizeAccess(accessRight);
        var existing = Grants.FirstOrDefault(g => g.AccountName.Equals(account, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.AccessRight = right;
        }
        else
        {
            Grants.Add(new ShareAclGrantItem { AccountName = account, AccessRight = right });
        }
        ValidationMessage = "";
    }

    [RelayCommand]
    private void RemoveGrant(ShareAclGrantItem? grant)
    {
        if (grant != null) Grants.Remove(grant);
    }

    public async Task<(bool Success, string Message)> SaveAsync()
    {
        if (IsSaving) return (false, "Save is already running.");
        if (string.IsNullOrWhiteSpace(ShareName))
        {
            ValidationMessage = "Enter a share name.";
            return (false, ValidationMessage);
        }
        if (IsCreate && string.IsNullOrWhiteSpace(FolderPath))
        {
            ValidationMessage = "Choose a folder to share.";
            return (false, ValidationMessage);
        }
        if (IsCreate && !Directory.Exists(FolderPath))
        {
            ValidationMessage = "The selected folder does not exist.";
            return (false, ValidationMessage);
        }
        if (Grants.Count == 0)
        {
            ValidationMessage = "Add at least one person with access.";
            return (false, ValidationMessage);
        }

        try
        {
            IsSaving = true;
            ValidationMessage = "";
            if (IsCreate)
            {
                var (ok, msg, _, _) = await _service.CreateShareAdvancedAsync(
                    ShareName, FolderPath, Description, (int)Math.Round(ConcurrentUserLimit),
                    CachingMode, EncryptData, FolderEnumerationMode,
                    Grants.ToList(), ApplyNtfs);
                return (ok, msg);
            }

            var (updated, updateMsg) = await _service.UpdateShareAdvancedAsync(
                ShareName, Description, (int)Math.Round(ConcurrentUserLimit),
                CachingMode, EncryptData, FolderEnumerationMode);
            if (!updated) return (false, updateMsg);
            var (aclOk, aclMsg) = await _service.ApplyShareAclAsync(ShareName, Grants.ToList());
            return (aclOk, aclOk ? "Share settings and permissions updated." : aclMsg);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string NormalizeAccess(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Equals("Full", StringComparison.OrdinalIgnoreCase) || v.Equals("Full control", StringComparison.OrdinalIgnoreCase)) return "Full";
        if (v.Equals("Change", StringComparison.OrdinalIgnoreCase) || v.Equals("Modify", StringComparison.OrdinalIgnoreCase)) return "Change";
        return "Read";
    }

    private static string NormalizeCachingDisplay(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Equals("None", StringComparison.OrdinalIgnoreCase)) return "No offline files";
        if (v.Equals("Manual", StringComparison.OrdinalIgnoreCase) || v.Equals("Documents", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Programs", StringComparison.OrdinalIgnoreCase) || v.Equals("BranchCache", StringComparison.OrdinalIgnoreCase)) return v;
        return "Manual";
    }

    private static string NormalizeEnumerationDisplay(string? value)
    {
        string v = (value ?? "").Trim();
        if (v.Equals("AccessBased", StringComparison.OrdinalIgnoreCase)) return "Access-based";
        if (v.Equals("HideUnspecified", StringComparison.OrdinalIgnoreCase)) return "Hide unspecified";
        if (v.Equals("Unrestricted", StringComparison.OrdinalIgnoreCase)) return "Unrestricted";
        return "Unrestricted";
    }
}
