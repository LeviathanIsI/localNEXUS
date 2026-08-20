using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The state of the Python runtime, and the three things a user can do about it.
/// </summary>
/// <remarks>
/// Provisioning runs on its own at first launch, so nothing here starts it in the normal case.
/// These commands exist for the case it went wrong: set it up again, rebuild it from scratch, or
/// look at what it wrote. A broken environment names what failed and offers a way out rather
/// than leaving the user with a model that silently will not run.
/// </remarks>
public sealed partial class PythonEnvironmentViewModel : ObservableObject
{
    private readonly PythonProvisioner _provisioner;
    private readonly IDialogService _dialogs;

    private CancellationTokenSource? _work;

    public PythonEnvironmentViewModel(PythonProvisioner provisioner, IDialogService dialogs)
    {
        _provisioner = provisioner;
        _dialogs = dialogs;

        _provisioner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PythonProvisioner.State))
            {
                OnPropertyChanged(nameof(CanWork));
                RepairCommand.NotifyCanExecuteChanged();
                ResetCommand.NotifyCanExecuteChanged();
            }
        };
    }

    /// <summary>The environment itself, bound directly for its state, stage and detail.</summary>
    public PythonProvisioner Environment => _provisioner;

    /// <summary>True when a command may run. Nothing may be started while one is already running.</summary>
    public bool CanWork => !_provisioner.IsBusy;

    /// <summary>Builds whatever is missing, reusing everything already downloaded.</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task RepairAsync()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();

        await _provisioner.RepairAsync(_work.Token).ConfigureAwait(false);
    }

    /// <summary>Deletes the environment and builds it again from the cached downloads.</summary>
    [RelayCommand(CanExecute = nameof(CanWork))]
    private async Task ResetAsync()
    {
        _work?.Cancel();
        _work = new CancellationTokenSource();

        await _provisioner.ResetAsync(_work.Token).ConfigureAwait(false);
    }

    /// <summary>Opens the folder the runtime lives in, which is where its logs and interpreter are.</summary>
    [RelayCommand]
    private void OpenFolder() => _dialogs.OpenFolderInExplorer(AppPaths.PythonRoot);
}
