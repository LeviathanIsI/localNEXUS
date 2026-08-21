using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Planning;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Writes the value arriving on its input pin to a file inside the opened Unity project.
/// </summary>
/// <remarks>
/// The path is always resolved through <see cref="Services.Files.UnityProjectService"/>, which
/// refuses anything that would land outside the project folder.
///
/// When a whole plan arrives rather than one file, every file is staged and the batch is written
/// together or not at all. A half applied change is worse than none: three of five scripts land,
/// the project does not compile, and undoing it has become the person's problem.
///
/// Before anything is written, each file is put through the Unity rules. Those are refusals
/// rather than warnings, because every one of them describes a change that compiles cleanly and
/// silently breaks a scene, and a warning in a feed nobody rereads is not a defence against a
/// prefab that lost its script.
/// </remarks>
public sealed partial class OutputNode : NodeBase
{
    /// <summary>Where Unity keeps script assets, and therefore the default destination.</summary>
    public const string DefaultSubfolder = "Assets/Scripts";

    /// <summary>Folder relative to the project root that the file is written into.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelativePathPreview))]
    private string _targetSubfolder = DefaultSubfolder;

    /// <summary>Name of the file to write, including its extension.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RelativePathPreview))]
    private string _fileName = "GeneratedScript.cs";

    /// <summary>When true, the run pauses and asks in the feed before the file is written.</summary>
    [ObservableProperty]
    private bool _askBeforeWriting;

    public OutputNode()
        : base("Output")
    {
        Content = AddInput("Code", PinType.Code);
    }

    /// <summary>Receives the content to write.</summary>
    public Pin Content { get; }

    /// <inheritdoc />
    public override string TypeKey => "Output";

    /// <summary>The destination as it will appear inside the project, for display.</summary>
    public string RelativePathPreview
        => string.IsNullOrWhiteSpace(FileName)
            ? "no file name set"
            : $"{TargetSubfolder?.Replace('\\', '/').Trim('/')}/{FileName}";

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        if (ctx.GetValue(Content) is IReadOnlyList<GeneratedFile> files)
        {
            return await WritePlanAsync(ctx, files, ct).ConfigureAwait(false);
        }

        var content = ctx.GetText(Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                $"{Title} received nothing to write. Connect a node to its Code pin.");
        }

        var project = ctx.Services.UnityProject;
        var absolutePath = project.ResolveTargetPath(TargetSubfolder ?? string.Empty, FileName);
        var displayPath = project.ToDisplayPath(absolutePath);

        if (AskBeforeWriting)
        {
            var approved = await ctx.Feed
                .RequestConfirmationAsync(
                    $"{Title}: write {displayPath}?",
                    $"{content.Length} characters will be written to{Environment.NewLine}{absolutePath}",
                    ct)
                .ConfigureAwait(false);

            if (!approved)
            {
                throw new OperationCanceledException($"Writing {displayPath} was declined.");
            }
        }

        // Read before writing so the feed can say how much changed rather than only how big the
        // result is. One extra read on a path that is about to write the same file anyway.
        var original = File.Exists(absolutePath)
            ? await File.ReadAllTextAsync(absolutePath, ct).ConfigureAwait(false)
            : null;

        var bytes = await ctx.Services.FileWriter.WriteAsync(absolutePath, content, ct).ConfigureAwait(false);
        var change = DiffStat.Between(original, content);

        StatusMessage = $"{displayPath}  ({bytes} bytes)";
        ctx.Feed.Add(ActivityKind.FileWritten, $"Wrote {displayPath}", absolutePath, Id).Detail =
            change.HasChange ? change.Text : $"{bytes} bytes";

        return NodeResult.Empty;
    }

    /// <summary>
    /// Writes every file of a plan, or none of them.
    /// </summary>
    private async Task<NodeResult> WritePlanAsync(
        NodeExecutionContext ctx,
        IReadOnlyList<GeneratedFile> files,
        CancellationToken ct)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"{Title} received an empty plan, so there was nothing to write.");
        }

        var project = ctx.Services.UnityProject;
        var index = ctx.Services.ProjectIndex;
        var batch = new ProjectWriteBatch(ctx.Services.FileWriter);

        // Staging is what makes the guardrails useful. Every file is checked before any of them
        // is written, so a plan whose fourth file would break a prefab writes none of the first
        // three either.
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var folder = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            var absolute = project.ResolveTargetPath(folder, Path.GetFileName(file.RelativePath));

            batch.EnforceExpectedExistence(absolute, file.Operation == FileOperation.Edit);
            UnityScriptRules.Enforce(file.RelativePath, file.Content, index.FindFile(file.RelativePath), file.Types);

            batch.Stage(absolute, file.Content);
        }

        if (AskBeforeWriting)
        {
            var listing = string.Join(Environment.NewLine, files.Select(f =>
                $"{(f.Operation == FileOperation.Create ? "create" : "edit")} {f.RelativePath}"));

            var approved = await ctx.Feed
                .RequestConfirmationAsync($"{Title}: write {files.Count} file(s)?", listing, ct)
                .ConfigureAwait(false);

            if (!approved)
            {
                throw new OperationCanceledException($"Writing {files.Count} file(s) was declined.");
            }
        }

        var written = await batch.CommitAsync(ct).ConfigureAwait(false);

        // Matched back by path rather than by position, because the batch writes one entry per
        // distinct path and a plan is allowed to name the same file twice.
        var changes = written.ToDictionary(w => w.Path, w => w.Change, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var folder = Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/') ?? string.Empty;
            var absolute = project.ResolveTargetPath(folder, Path.GetFileName(file.RelativePath));

            var entry = ctx.Feed.Add(
                ActivityKind.FileWritten,
                $"{(file.Operation == FileOperation.Create ? "Wrote" : "Edited")} {file.RelativePath}",
                null,
                Id);

            // How much changed, so that a three line fix and a rewrite do not read the same.
            if (changes.TryGetValue(Path.GetFullPath(absolute), out var change) && change.HasChange)
            {
                entry.Detail = change.Text;
            }

            if (UnityScriptRules.DescribeAttachmentNeeded(file.Types) is { } note)
            {
                ctx.Feed.Info($"{file.RelativePath} needs attaching", note);
            }
        }

        // The index is now out of date by exactly the files just written, and the cheapest correct
        // answer is to let the next run notice their timestamps changed.
        var bytes = written.Sum(w => w.Bytes);
        StatusMessage = $"{written.Count} file(s), {bytes} bytes";

        return NodeResult.Empty;
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["targetSubfolder"] = TargetSubfolder,
        ["fileName"] = FileName,
        ["askBeforeWriting"] = AskBeforeWriting
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        TargetSubfolder = settings["targetSubfolder"]?.GetValue<string>() ?? DefaultSubfolder;
        FileName = settings["fileName"]?.GetValue<string>() ?? "GeneratedScript.cs";
        AskBeforeWriting = settings["askBeforeWriting"]?.GetValue<bool>() ?? false;
    }
}
