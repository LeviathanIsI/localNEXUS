using System.IO;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>What looking for a model's multimodal projector found.</summary>
public enum ProjectorState
{
    /// <summary>A projector was found beside the model and can be passed to the server.</summary>
    Found,

    /// <summary>The path is not a GGUF, so there is nothing here to look beside.</summary>
    NotAGguf,

    /// <summary>The chosen file is itself a projector rather than a model.</summary>
    ModelIsAProjector,

    /// <summary>Nothing beside the model declares a vision encoder.</summary>
    NotFound
}

/// <summary>
/// The answer to whether a local model can see, and what to launch it with.
/// </summary>
/// <param name="State">Which of the four answers this is.</param>
/// <param name="Path">The projector, when one was found. Null otherwise.</param>
/// <param name="Message">What to tell a person, whether or not it worked.</param>
public sealed record ProjectorLookup(ProjectorState State, string? Path, string Message)
{
    /// <summary>True when this model can be served as a vision model.</summary>
    public bool IsUsable => State == ProjectorState.Found;
}

/// <summary>
/// Finds the multimodal projector that belongs to a local vision model.
/// </summary>
/// <remarks>
/// llama.cpp serves vision through a projector file alongside the weights, and a vision GGUF
/// started without one loads perfectly and then answers 400 to every image. That is a launch
/// argument, so it is found here rather than asked of the user.
///
/// The convention is a second GGUF in the same folder, published beside the weights by whoever
/// quantised them and named <c>mmproj-*.gguf</c> far more often than not. The name is used to
/// decide what to look at first and never to decide the answer, which is the same rule
/// <see cref="ModelFormatDetector"/> follows: a name is a thing anybody can change, and what is
/// inside the file is not. What settles it is <c>clip.has_vision_encoder</c> in the header, which
/// is the key llama.cpp's own loader reads.
///
/// The same folder only. A projector is published with the weights it was made for and pairing one
/// with a model from somewhere else produces a server that starts and then talks nonsense, so
/// widening the search would mostly widen the ways of being wrong.
/// </remarks>
public static class VisionProjectorLocator
{
    /// <summary>Fragments that mean a file is worth looking inside first.</summary>
    /// <remarks>
    /// Ordering only. Every candidate is read either way; this is what stops a folder of large
    /// models being opened one by one before reaching the small file that was obviously it.
    /// </remarks>
    private static readonly string[] NameHints = { "mmproj", "projector", "-proj", "clip", "vision" };

    /// <summary>How many siblings one lookup will read before it stops.</summary>
    /// <remarks>
    /// A guard against somebody keeping two hundred models in one folder, not against a normal
    /// layout, which is one or two files. It is said in the message when it is reached, because a
    /// search that quietly stopped early is worse than one that failed.
    /// </remarks>
    public const int MaxCandidates = 32;

    /// <summary>
    /// Looks for the projector belonging to the model at the given path.
    /// </summary>
    /// <remarks>Never throws. An unreadable folder is an answer, not a failure.</remarks>
    public static ProjectorLookup Locate(string? modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return new ProjectorLookup(ProjectorState.NotAGguf, null, "No model was chosen.");
        }

        var model = ModelFormatDetector.Describe(modelPath);

        if (model.Format != ModelFormat.Gguf)
        {
            return new ProjectorLookup(
                ProjectorState.NotAGguf,
                null,
                $"{model.DisplayName} is {model.FormatLabel}. A local vision model is a GGUF, because the "
                + "projector that lets it see is a llama.cpp thing.");
        }

        var full = System.IO.Path.GetFullPath(modelPath);

        if (GgufMetadata.Read(full) is { HasVisionEncoder: true } itself)
        {
            return new ProjectorLookup(
                ProjectorState.ModelIsAProjector,
                null,
                $"{model.DisplayName} is the projector itself{Kind(itself)}, not a model. Choose the weights it "
                + "was published beside and this will find it on its own.");
        }

        var folder = System.IO.Path.GetDirectoryName(full);

        if (folder is null)
        {
            return new ProjectorLookup(ProjectorState.NotFound, null, $"{model.DisplayName} has no folder to search.");
        }

        string[] siblings;

        try
        {
            siblings = Directory.GetFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ProjectorLookup(ProjectorState.NotFound, null, $"{folder} could not be read: {ex.Message}");
        }

        var candidates = siblings
            .Where(f => !string.Equals(System.IO.Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(Looks)
            .ThenBy(Size)
            .ToList();

        var budget = Math.Min(candidates.Count, MaxCandidates);

        for (var i = 0; i < budget; i++)
        {
            if (GgufMetadata.Read(candidates[i]) is { HasVisionEncoder: true } projector)
            {
                return new ProjectorLookup(
                    ProjectorState.Found,
                    candidates[i],
                    $"{System.IO.Path.GetFileName(candidates[i])}{Kind(projector)}, found beside {model.DisplayName}.");
            }
        }

        var stopped = candidates.Count > MaxCandidates
            ? $" Only the first {MaxCandidates} of {candidates.Count} files there were read."
            : string.Empty;

        return new ProjectorLookup(
            ProjectorState.NotFound,
            null,
            $"{model.DisplayName} has no multimodal projector beside it, so it cannot read images. A vision "
            + $"model is published as two files: the weights, and an mmproj file that holds the vision encoder. "
            + $"Download the mmproj that goes with this model into {folder} and choose it again.{stopped}");
    }

    /// <summary>The projector kind in brackets, when the file said what it was.</summary>
    private static string Kind(GgufHeader header)
        => header.ProjectorType is { Length: > 0 } type ? $" ({type})" : string.Empty;

    /// <summary>True when the name says this is probably the projector.</summary>
    private static bool Looks(string path)
    {
        var name = System.IO.Path.GetFileName(path);

        return NameHints.Any(hint => name.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Size on disk, used to read the small files first. A projector is the small one.</summary>
    private static long Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return long.MaxValue;
        }
    }
}
