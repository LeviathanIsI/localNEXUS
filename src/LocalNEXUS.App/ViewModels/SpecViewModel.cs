using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Spec;

namespace LocalNEXUS.App.ViewModels;

/// <summary>Where the tab has got to, which is not the same as whether anything failed.</summary>
public enum SpecTabState
{
    /// <summary>Nothing has been asked for yet.</summary>
    Idle,

    /// <summary>Waiting on the worker.</summary>
    Reading,

    /// <summary>There are changes to show.</summary>
    Ready,

    /// <summary>The worker answered and there is nothing to show, which is not a failure.</summary>
    Empty,

    /// <summary>The worker could not be reached or refused.</summary>
    Unreachable
}

/// <summary>
/// The Spec tab: changes, their artifacts, and what to do about them.
/// </summary>
/// <remarks>
/// This renders and the extension supplies. Nothing here works out which artifact comes next,
/// whether a change is complete, or what a spec delta merges to, because every one of those is what
/// OpenSpec is, and a second implementation of its state model would drift from it and be wrong in
/// a way nobody would notice until it mattered. When the tab needs to know something, it asks.
///
/// The handoff to the Workspace is the reason this is a tab rather than a terminal. A change's task
/// list goes across as a request, whole rather than a task at a time, and that choice is argued in
/// the command itself.
/// </remarks>
public sealed partial class SpecViewModel : ObservableObject
{
    private readonly ExtensionRegistry _extensions;
    private readonly ExtensionHost _host;
    private readonly IActivityFeed _feed;
    private readonly Action<string> _sendToWorkspace;

    /// <summary>Where the tab has got to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReading))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    private SpecTabState _state = SpecTabState.Idle;

    /// <summary>What to say under the list, whatever state it is in.</summary>
    [ObservableProperty]
    private string _statusText = "Nothing read yet.";

    /// <summary>What the worker said it is bridging to, once it has said.</summary>
    [ObservableProperty]
    private string? _workerText;

    /// <summary>The change being looked at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(AdvanceCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendTasksToWorkspaceCommand))]
    private SpecChange? _selectedChange;

    /// <summary>The artifact being read.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtifact))]
    private SpecArtifact? _selectedArtifact;

    /// <summary>What that artifact holds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArtifact))]
    private string _artifactText = string.Empty;

    /// <summary>Where it lives, for somebody who wants to open it properly.</summary>
    [ObservableProperty]
    private string? _artifactPath;

    public SpecViewModel(
        ExtensionRegistry extensions,
        ExtensionHost host,
        IActivityFeed feed,
        Action<string> sendToWorkspace)
    {
        _extensions = extensions;
        _host = host;
        _feed = feed;
        _sendToWorkspace = sendToWorkspace;
    }

    /// <summary>Every change the tool reported, active first.</summary>
    public ObservableCollection<SpecChange> Changes { get; } = new();

    /// <summary>True while the worker is being waited on.</summary>
    public bool IsReading => State == SpecTabState.Reading;

    /// <summary>True when a change is selected.</summary>
    public bool HasSelection => SelectedChange is not null;

    /// <summary>True when there is artifact text to show.</summary>
    public bool HasArtifact => SelectedArtifact is not null && ArtifactText.Length > 0;

    /// <summary>
    /// The task list of the selected change, when it has one that has been written.
    /// </summary>
    /// <remarks>
    /// Matched on the artifact rather than on a file name, because which artifact is the task list
    /// is the worker's business and OpenSpec is free to call it something else.
    /// </remarks>
    public SpecArtifact? Tasks => SelectedChange?.Artifacts
        .FirstOrDefault(a => a.Id.Contains("task", StringComparison.OrdinalIgnoreCase)
                             || a.Name.Contains("task", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reads the changes again.</summary>
    [RelayCommand(CanExecute = nameof(CanRead))]
    private async Task RefreshAsync(CancellationToken ct)
    {
        await WithWorkerAsync(
            async client =>
            {
                var info = await client.DescribeAsync(ct).ConfigureAwait(false);

                WorkerText = info.Root is { Length: > 0 } root
                    ? $"{info.Tool} {info.Version}, reading {root}"
                    : $"{info.Tool} {info.Version}";

                var changes = await client.ListChangesAsync(ct).ConfigureAwait(false);

                Changes.Clear();

                foreach (var change in changes
                    .OrderBy(c => c.Status == SpecChangeStatus.Archived)
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    Changes.Add(change);
                }

                SelectedChange = Changes.FirstOrDefault(c => c.Status == SpecChangeStatus.Active)
                                 ?? Changes.FirstOrDefault();

                var active = Changes.Count(c => c.Status == SpecChangeStatus.Active);

                State = Changes.Count == 0 ? SpecTabState.Empty : SpecTabState.Ready;

                StatusText = Changes.Count == 0
                    ? "No changes yet. Propose one with OpenSpec and it will appear here."
                    : $"{active} active, {Changes.Count - active} archived.";
            },
            ct).ConfigureAwait(true);
    }

    /// <summary>Reads one artifact so it can be looked at.</summary>
    [RelayCommand]
    private async Task ShowArtifactAsync(SpecArtifact? artifact, CancellationToken ct)
    {
        if (artifact is null || SelectedChange is not { } change)
        {
            return;
        }

        SelectedArtifact = artifact;

        // A blocked artifact is not there to read yet, and asking for it would be asking the worker
        // for a file that does not exist. Saying why is the answer.
        if (artifact.State == SpecArtifactState.Blocked)
        {
            ArtifactPath = null;
            ArtifactText = artifact.Detail is { Length: > 0 } detail
                ? $"{artifact.Name} is blocked: {detail}"
                : $"{artifact.Name} is waiting on something earlier in this change, so there is nothing to read yet.";

            OnPropertyChanged(nameof(HasArtifact));
            return;
        }

        await WithWorkerAsync(
            async client =>
            {
                var content = await client
                    .ReadArtifactAsync(change.Id, artifact.Id, ct)
                    .ConfigureAwait(false);

                ArtifactText = content.Content;
                ArtifactPath = content.Path;
                State = SpecTabState.Ready;
            },
            ct).ConfigureAwait(true);
    }

    /// <summary>Asks the tool to write the next artifact that is ready.</summary>
    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private async Task AdvanceAsync(CancellationToken ct)
    {
        if (SelectedChange is not { } change)
        {
            return;
        }

        await WithWorkerAsync(
            async client =>
            {
                var (message, updated) = await client.AdvanceAsync(change.Id, ct).ConfigureAwait(false);

                _feed.Info($"OpenSpec: {change.Name}", message);

                if (updated is not null)
                {
                    var index = Changes.IndexOf(change);

                    if (index >= 0)
                    {
                        Changes[index] = updated;
                    }

                    SelectedChange = updated;
                }

                State = SpecTabState.Ready;
                StatusText = message;
            },
            ct).ConfigureAwait(true);
    }

    /// <summary>
    /// Sends the selected change's task list to the Workspace as a request.
    /// </summary>
    /// <remarks>
    /// The whole checklist as one request, rather than a task at a time, and the reason is what the
    /// two halves are each good at. OpenSpec has already ordered the tasks and scoped them to one
    /// change; the Triage node is built to turn one request into an ordered multi file plan, and
    /// the compile check compiles each generated file against the ones settled before it. Sending a
    /// task at a time would throw away the ordering OpenSpec computed, replace it with whatever
    /// order somebody clicked in, and make every task a separate run that cannot see the files the
    /// previous one wrote.
    ///
    /// The cost is that a long checklist meets the context budget, which drops what does not fit in
    /// rank order and says so in the feed. That is a limit somebody can see and act on, which the
    /// silent alternative is not.
    ///
    /// It arrives the way anything typed arrives. The Workspace is not changed by any of this and
    /// does not know where the text came from.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanSendTasks))]
    private async Task SendTasksToWorkspaceAsync(CancellationToken ct)
    {
        if (SelectedChange is not { } change || Tasks is not { } tasks)
        {
            return;
        }

        await WithWorkerAsync(
            async client =>
            {
                var content = await client.ReadArtifactAsync(change.Id, tasks.Id, ct).ConfigureAwait(false);

                if (content.Content.Trim().Length == 0)
                {
                    StatusText = $"{tasks.Name} is empty, so there was nothing to send.";
                    return;
                }

                _sendToWorkspace(
                    $"Implement the change '{change.Name}'. This is its task list:"
                    + Environment.NewLine + Environment.NewLine
                    + content.Content.Trim());

                State = SpecTabState.Ready;
                StatusText = $"Sent {tasks.Name} to the Workspace. Press Run there when you are ready.";

                _feed.Info(
                    $"OpenSpec: {change.Name} sent to the Workspace",
                    "The task list is in the request box. Nothing has run yet.");
            },
            ct).ConfigureAwait(true);
    }

    private bool CanRead() => State != SpecTabState.Reading;

    private bool CanAdvance()
        => State != SpecTabState.Reading
           && SelectedChange is { Status: SpecChangeStatus.Active, NextReady: not null };

    private bool CanSendTasks()
        => State != SpecTabState.Reading
           && Tasks is { State: SpecArtifactState.Done };

    partial void OnSelectedChangeChanged(SpecChange? value)
    {
        SelectedArtifact = null;
        ArtifactText = string.Empty;
        ArtifactPath = null;

        OnPropertyChanged(nameof(Tasks));
        SendTasksToWorkspaceCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Starts the extension if it is not up, does the work, and turns every failure into a state.
    /// </summary>
    /// <remarks>
    /// Unreachable is a state rather than an error dialog. An extension that is not installed, a
    /// Node that is not on the path and a worker that died are all things the tab can say plainly
    /// and carry on from, and none of them is worth interrupting somebody with a box.
    /// </remarks>
    private async Task WithWorkerAsync(Func<SpecWorkerClient, Task> work, CancellationToken ct)
    {
        if (Installed() is not { } extension)
        {
            State = SpecTabState.Unreachable;
            StatusText = "The OpenSpec extension is not installed. Add it from the Extensions window.";
            return;
        }

        State = SpecTabState.Reading;
        StatusText = "Asking OpenSpec.";

        try
        {
            var session = await _host
                .EnsureRunningAsync(extension, ExtensionContract.Spec, ct)
                .ConfigureAwait(true);

            await work(new SpecWorkerClient(session)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            State = SpecTabState.Idle;
            StatusText = "Stopped.";
        }
        catch (ExtensionException ex)
        {
            State = SpecTabState.Unreachable;
            StatusText = ex.Message;
            _feed.Error("OpenSpec could not be reached", ex.Message);
        }
    }

    /// <summary>The installed extension that declares the spec contract, or null.</summary>
    public InstalledExtension? Installed()
        => _extensions.Extensions.FirstOrDefault(e => e.Manifest.ProvidesTab && e.IsUsable);
}
