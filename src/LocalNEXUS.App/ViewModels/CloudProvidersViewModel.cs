using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>One provider as the settings list draws it.</summary>
public sealed partial class ProviderRowViewModel : ObservableObject
{
    /// <summary>The key as typed. Never persisted from here; the store encrypts it.</summary>
    [ObservableProperty]
    private string _keyEntry = string.Empty;

    /// <summary>True once a key exists for this provider.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _hasKey;

    public ProviderRowViewModel(CloudProvider provider, bool hasKey)
    {
        Provider = provider;
        _hasKey = hasKey;
    }

    /// <summary>The catalogue entry.</summary>
    public CloudProvider Provider { get; }

    /// <summary>What the row says about its state.</summary>
    /// <remarks>
    /// Not configured rather than not working, because a provider nobody has set up has not
    /// failed at anything.
    /// </remarks>
    public string StatusText => HasKey ? "key saved" : "no key yet";

    /// <summary>True when there is somewhere to send someone for a key.</summary>
    public bool HasKeyUrl => !string.IsNullOrWhiteSpace(Provider.KeyUrl);
}

/// <summary>
/// The hosted providers, their keys, and the spending threshold.
/// </summary>
/// <remarks>
/// Lives in the Models section of settings because a hosted provider is a kind of model source,
/// which is the same reason the local folders and the mesh live there.
/// </remarks>
public sealed partial class CloudProvidersViewModel : ObservableObject
{
    private readonly ICredentialStore _credentials;
    private readonly AppConfig _config;
    private readonly IDialogService _dialogs;
    private readonly IActivityFeed _feed;

    /// <summary>Name for a custom endpoint being added.</summary>
    [ObservableProperty]
    private string _customName = string.Empty;

    /// <summary>Base url for a custom endpoint being added.</summary>
    [ObservableProperty]
    private string _customBaseUrl = string.Empty;

    public CloudProvidersViewModel(
        ICredentialStore credentials,
        AppConfig config,
        IDialogService dialogs,
        IActivityFeed feed)
    {
        _credentials = credentials;
        _config = config;
        _dialogs = dialogs;
        _feed = feed;

        Providers = new ObservableCollection<ProviderRowViewModel>(
            ProviderCatalog.All.Select(p => new ProviderRowViewModel(p, credentials.Has(p.Id))));

        foreach (var custom in config.CustomProviders)
        {
            var provider = ProviderCatalog.Custom(custom.Name, custom.BaseUrl);
            Providers.Add(new ProviderRowViewModel(provider, credentials.Has(provider.Id)));
        }
    }

    /// <summary>Every provider, shipped and custom.</summary>
    public ObservableCollection<ProviderRowViewModel> Providers { get; }

    /// <summary>
    /// What a run may cost before it warns, in dollars. Zero switches the warning off.
    /// </summary>
    public decimal CostWarningThreshold
    {
        get => _config.CostWarningThreshold;
        set
        {
            if (_config.CostWarningThreshold == value)
            {
                return;
            }

            _config.CostWarningThreshold = value < 0m ? 0m : value;
            _config.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdSummary));
        }
    }

    /// <summary>What the threshold setting reads as.</summary>
    public string ThresholdSummary => CostWarningThreshold <= 0m
        ? "No warning. Runs start whatever they might cost."
        : $"A run that could cost more than {RunCost.Format(CostWarningThreshold)} asks first.";

    /// <summary>Saves a typed key into the encrypted store.</summary>
    [RelayCommand]
    private void SaveKey(ProviderRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.KeyEntry))
        {
            return;
        }

        _credentials.Set(row.Provider.Id, row.KeyEntry);
        row.HasKey = _credentials.Has(row.Provider.Id);

        // Cleared from the box the moment it is stored, so it is not left sitting on screen.
        row.KeyEntry = string.Empty;

        _feed.Info($"{row.Provider.DisplayName} key saved", "Encrypted for this Windows account. It is never written into a graph.");
    }

    /// <summary>Forgets a key.</summary>
    [RelayCommand]
    private void ClearKey(ProviderRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _credentials.Remove(row.Provider.Id);
        row.HasKey = false;
        row.KeyEntry = string.Empty;
    }

    /// <summary>Opens the provider's page for getting a key.</summary>
    [RelayCommand]
    private void GetKey(ProviderRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Provider.KeyUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = row.Provider.KeyUrl, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            _dialogs.ShowError("Could not open the browser", row.Provider.KeyUrl);
        }
    }

    /// <summary>
    /// Adds an OpenAI compatible endpoint by url.
    /// </summary>
    /// <remarks>
    /// The escape hatch. A provider nobody here anticipated works without a code change, which is
    /// what stops this list becoming a treadmill.
    /// </remarks>
    [RelayCommand]
    private void AddCustom()
    {
        if (string.IsNullOrWhiteSpace(CustomBaseUrl))
        {
            _dialogs.ShowError("No address given", "A custom endpoint needs the base url of its API.");
            return;
        }

        var provider = ProviderCatalog.Custom(CustomName, CustomBaseUrl);

        if (Providers.Any(p => string.Equals(p.Provider.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.ShowError("Already added", $"There is already an endpoint called {provider.DisplayName}.");
            return;
        }

        _config.CustomProviders.Add(new CustomProviderRecord(provider.DisplayName, provider.BaseUrl));
        _config.Save();

        Providers.Add(new ProviderRowViewModel(provider, hasKey: false));

        CustomName = string.Empty;
        CustomBaseUrl = string.Empty;
    }

    /// <summary>Removes a custom endpoint and its key.</summary>
    [RelayCommand]
    private void RemoveCustom(ProviderRowViewModel? row)
    {
        if (row is null || !row.Provider.IsCustom)
        {
            return;
        }

        _credentials.Remove(row.Provider.Id);
        Providers.Remove(row);

        var stored = _config.CustomProviders
            .FirstOrDefault(c => string.Equals(c.Name, row.Provider.DisplayName, StringComparison.OrdinalIgnoreCase));

        if (stored is not null)
        {
            _config.CustomProviders.Remove(stored);
            _config.Save();
        }
    }
}
