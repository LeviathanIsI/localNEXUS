using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Writes the value arriving on its input pin to a file inside the opened Unity project.
/// </summary>
/// <remarks>
/// The path is always resolved through <see cref="Services.Files.UnityProjectService"/>, which
/// refuses anything that would land outside the project folder. Only creation and overwrite are
/// supported here; editing and deleting existing files are separate concerns for later.
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
        : base("Write File")
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

        var bytes = await ctx.Services.FileWriter.WriteAsync(absolutePath, content, ct).ConfigureAwait(false);

        StatusMessage = $"{displayPath}  ({bytes} bytes)";
        ctx.Feed.Add(ActivityKind.FileWritten, $"Wrote {displayPath}", absolutePath, Id).Detail = $"{bytes} bytes";

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
