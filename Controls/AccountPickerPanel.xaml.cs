using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using DiskMasterWinUI.Models;
using DiskMasterWinUI.Services;

namespace DiskMasterWinUI.Controls;

/// <summary>
/// Shared account picker. The account columns stay hidden until the user chooses
/// Administrator or User; the host page receives the selected account through an event.
/// </summary>
public sealed partial class AccountPickerPanel : UserControl
{
    private readonly LocalAccountService _service = new();
    private readonly ObservableCollection<LocalAccountItem> _allAccounts = new();
    private readonly ObservableCollection<LocalAccountItem> _visibleAccounts = new();
    private string _scope = "";
    private readonly bool _isReady;

    public LocalAccountItem? SelectedAccount { get; private set; }
    public event EventHandler<LocalAccountItem?>? SelectedAccountChanged;

    public AccountPickerPanel()
    {
        InitializeComponent();
        AccountList.ItemsSource = _visibleAccounts;
        _isReady = true;
    }

    private async void AdminScopeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        await LoadScopeAsync("Administrators");
    }

    private async void UserScopeRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        await LoadScopeAsync("Users");
    }

    private async Task LoadScopeAsync(string group)
    {
        _scope = group;
        SelectedAccount = null;
        AccountList.SelectedItem = null;
        SelectedChip.Visibility = Visibility.Collapsed;
        _allAccounts.Clear();
        _visibleAccounts.Clear();
        SearchRow.Visibility = Visibility.Visible;
        HeaderRow.Visibility = Visibility.Collapsed;
        AccountList.Visibility = Visibility.Collapsed;
        HintText.Text = "Loading accounts...";
        LoadingRing.IsActive = true;

        try
        {
            var accounts = await _service.GetGroupMembersAsync(group);
            foreach (var account in accounts) _allAccounts.Add(account);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            HintText.Text = $"Unable to load accounts: {ex.Message}";
            DebugLogService.Instance.Warning($"Account picker load failed: {ex.Message}", "LocalAccount");
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? "";
        _visibleAccounts.Clear();
        foreach (var account in _allAccounts)
        {
            if (string.IsNullOrEmpty(query) ||
                account.AccountName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                account.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                account.Sid.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                account.AccountType.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _visibleAccounts.Add(account);
            }
        }

        bool hasAccounts = _visibleAccounts.Count > 0;
        HeaderRow.Visibility = hasAccounts ? Visibility.Visible : Visibility.Collapsed;
        AccountList.Visibility = hasAccounts ? Visibility.Visible : Visibility.Collapsed;

        if (hasAccounts)
        {
            HintText.Text = $"{_visibleAccounts.Count} account(s) in {_scope}.";
        }
        else if (_allAccounts.Count == 0)
        {
            HintText.Text = "No accounts found in this scope, or access was denied.";
        }
        else
        {
            HintText.Text = "No accounts match. Clear the search or switch scope.";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isReady) ApplyFilter();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _service.InvalidateCache();
        if (!string.IsNullOrEmpty(_scope)) await LoadScopeAsync(_scope);
    }

    private void AccountList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is LocalAccountItem account)
        {
            AutomationProperties.SetName(args.ItemContainer,
                $"{account.AccountName}, {account.AccountType}, {account.StateText}");
        }
    }

    private void AccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedAccount = AccountList.SelectedItem as LocalAccountItem;
        if (SelectedAccount == null)
        {
            SelectedChip.Visibility = Visibility.Collapsed;
        }
        else
        {
            SelectedText.Text = $"{SelectedAccount.AccountName} ({SelectedAccount.AccountType})";
            SelectedChip.Visibility = Visibility.Visible;
        }
        SelectedAccountChanged?.Invoke(this, SelectedAccount);
    }
}
