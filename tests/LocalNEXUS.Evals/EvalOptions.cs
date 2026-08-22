using System.IO;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.Evals;

/// <summary>
/// What to run, against what, and where the answers go.
/// </summary>
/// <remarks>
/// The models come from here rather than from anything compiled in. Running the same task set
/// against a small model and a large one is most of the point of having a task set: a shape that
/// only the large one gets right is the model, and a shape both get wrong the same way is the
/// prompt, the ranking or the budget, which is the part that can actually be fixed.
/// </remarks>
public sealed class EvalOptions
{
    /// <summary>Models to run, in order. Empty means every one that can be found.</summary>
    public IReadOnlyList<string> Models { get; init; } = Array.Empty<string>();

    /// <summary>Tasks to run by identifier. Empty means all of them.</summary>
    public IReadOnlyList<string> Tasks { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Which task sets to run.
    /// </summary>
    /// <remarks>
    /// Nothing named means the Unity set, so every command line written before the plain set
    /// existed produces exactly what it did. That is not politeness; it is what keeps the Unity
    /// numbers comparable with the runs already recorded.
    /// </remarks>
    public TaskSetChoice Sets { get; init; } = TaskSetChoice.None;

    /// <summary>
    /// How many times to run the whole set.
    /// </summary>
    /// <remarks>
    /// One by default because a full pass is slow. More than one is how a rate becomes meaningful:
    /// a model is not deterministic, and a task that works four times in five is a different thing
    /// from one that works once.
    /// </remarks>
    public int Repeats { get; init; } = 1;

    /// <summary>Where results are written.</summary>
    public string OutputDirectory { get; init; } = DefaultOutputDirectory();

    /// <summary>
    /// Where results go when nobody says otherwise, which is deliberately not the application's
    /// own data folder.
    /// </summary>
    /// <remarks>
    /// It used to be a folder inside <c>%LOCALAPPDATA%\LocalNEXUS</c>, and that was a mistake of
    /// exactly the kind this file is supposed to help find. Results are the one thing here whose
    /// whole value is accumulating across weeks, and putting them among the models, the config and
    /// the credentials meant anything clearing the application's data took the record with it.
    /// That happened three times in one session, and <c>history.csv</c> went with it every time.
    ///
    /// The repository is the right home: it outlives a run, it is where the harness is invoked
    /// from, and nothing that tidies up application data can reach it. When there is no repository
    /// to find, which is a published copy of the harness, results sit beside the executable
    /// instead. Neither is inside the application's data folder, which is the whole point.
    /// </remarks>
    public static string DefaultOutputDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LocalNEXUS.sln")))
            {
                return Path.Combine(directory.FullName, "evals");
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "evals");
    }

    /// <summary>Context window the server is started with.</summary>
    public int ContextSize { get; init; } = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>Layers offloaded to the GPU.</summary>
    public int GpuLayers { get; init; } = LlamaLaunchOptions.DefaultGpuLayers;

    /// <summary>Ceiling on one reply.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>Sampling temperature for the coder.</summary>
    public double Temperature { get; init; } = 0.2d;

    /// <summary>How many times the compiler check may ask for a fix.</summary>
    public int RetryLimit { get; init; } = 2;

    /// <summary>
    /// Run one debate and one judgement and print the transcript, instead of the task set.
    /// </summary>
    /// <remarks>
    /// Separate from the scored tasks because a debate produces a brief rather than a file, so
    /// there is nothing on disk afterwards for a task to measure. What it produces is something
    /// to read.
    /// </remarks>
    public bool DebateOnly { get; init; }

    /// <summary>
    /// Print what the planner replies for the named tasks, instead of scoring anything.
    /// </summary>
    /// <remarks>
    /// A task that fails every time fails for a reason, and Triage parses the reply and discards
    /// it, so the reason is nowhere. This puts it somewhere.
    /// </remarks>
    public bool DiagnoseOnly { get; init; }

    /// <summary>Everything the models folder holds, in a stable order.</summary>
    public static IReadOnlyList<string> DiscoverModels()
    {
        if (!Directory.Exists(AppPaths.Models))
        {
            return Array.Empty<string>();
        }

        return Directory
            .EnumerateFiles(AppPaths.Models, "*.gguf", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The models to actually run: whatever was named, matched against what is on disk.
    /// </summary>
    /// <remarks>
    /// A name is matched as a fragment of the file name so that the whole path never has to be
    /// typed, and an absolute path that exists is taken as given so a model outside the usual
    /// folder can still be measured.
    /// </remarks>
    public IReadOnlyList<string> ResolveModels(out IReadOnlyList<string> unmatched)
    {
        var available = DiscoverModels();

        if (Models.Count == 0)
        {
            unmatched = Array.Empty<string>();
            return available;
        }

        var chosen = new List<string>();
        var missing = new List<string>();

        foreach (var wanted in Models)
        {
            if (File.Exists(wanted))
            {
                chosen.Add(Path.GetFullPath(wanted));
                continue;
            }

            var match = available.FirstOrDefault(p =>
                Path.GetFileName(p).Contains(wanted, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                missing.Add(wanted);
                continue;
            }

            chosen.Add(match);
        }

        unmatched = missing;
        return chosen;
    }

    /// <summary>Reads the command line, or explains it.</summary>
    public static bool TryParse(string[] args, out EvalOptions options, out string? error)
    {
        var models = new List<string>();
        var tasks = new List<string>();

        var repeats = 1;
        var output = DefaultOutputDirectory();
        var context = LlamaLaunchOptions.DefaultContextSize;
        var gpuLayers = LlamaLaunchOptions.DefaultGpuLayers;
        var maxTokens = 4096;
        var temperature = 0.2d;
        var retries = 2;
        var debate = false;
        var diagnose = false;
        var sets = TaskSetChoice.None;

        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            string? Value() => i + 1 < args.Length ? args[++i] : null;

            switch (name)
            {
                case "--models":
                case "--model":
                    models.AddRange(Split(Value()));
                    break;

                case "--tasks":
                case "--task":
                    tasks.AddRange(Split(Value()));
                    break;

                case "--repeats":
                    if (!int.TryParse(Value(), out repeats) || repeats < 1)
                    {
                        options = new EvalOptions();
                        error = "--repeats needs a whole number of one or more.";
                        return false;
                    }

                    break;

                case "--out":
                    output = Value() ?? output;
                    break;

                case "--context":
                    int.TryParse(Value(), out context);
                    break;

                case "--gpu-layers":
                    int.TryParse(Value(), out gpuLayers);
                    break;

                case "--max-tokens":
                    int.TryParse(Value(), out maxTokens);
                    break;

                case "--temperature":
                    double.TryParse(Value(), out temperature);
                    break;

                case "--retries":
                    int.TryParse(Value(), out retries);
                    break;

                case "--unity":
                    sets |= TaskSetChoice.Unity;
                    break;

                case "--plain":
                    sets |= TaskSetChoice.Plain;
                    break;

                case "--debate":
                    debate = true;
                    break;

                case "--diagnose":
                    diagnose = true;
                    break;

                default:
                    options = new EvalOptions();
                    error = $"Unrecognised argument '{name}'.";
                    return false;
            }
        }

        options = new EvalOptions
        {
            Models = models,
            Tasks = tasks,
            Repeats = repeats,
            OutputDirectory = output,
            ContextSize = context,
            GpuLayers = gpuLayers,
            MaxTokens = maxTokens,
            Temperature = temperature,
            RetryLimit = retries,
            DebateOnly = debate,
            DiagnoseOnly = diagnose,
            Sets = sets
        };

        error = null;
        return true;
    }

    private static IEnumerable<string> Split(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>What the harness accepts, printed when it is asked for something it does not.</summary>
    public const string Usage = """
        LocalNEXUS eval harness

          --models <a,b>      Models to run. A fragment of the file name is enough, or give a
                              full path. Defaults to every GGUF under the models folder.
          --tasks <a,b>       Task identifiers to run. Defaults to all of them.
          --unity             Run the Unity task set. The default when neither is named, so a
                              command line written before the plain set existed is unchanged.
          --plain             Run the plain C# task set, against a generated csproj project with
                              no Assets folder and nothing Unity would recognise. Give both flags
                              to run both, though the totals of a mixed run are a mixture and the
                              two sets are not comparable with each other.
          --repeats <n>       How many times to run the set. Defaults to 1. More than one is how
                              a rate becomes meaningful, because a model is not deterministic.
          --out <folder>      Where results are written. Defaults to an evals folder in the
                              repository, deliberately not the application's data directory, so
                              that clearing app data cannot take the record with it.
          --context <n>       Context window the server is started with.
          --gpu-layers <n>    Layers offloaded to the GPU.
          --max-tokens <n>    Ceiling on one reply.
          --temperature <n>   Sampling temperature for the coder.
          --retries <n>       How many times the compiler check may ask for a fix.
          --diagnose          Print what the planner replies for the selected tasks, and what
                              each parser makes of it. Scores nothing and writes nothing into a
                              project. Use with --tasks.
          --debate            Run one debate and one judgement and print the transcript,
                              instead of the task set. Nothing is scored and nothing is
                              written into a project; the output is something to read.

        This is not a gate and not part of any build. It is slow, it needs a model present, and it
        downloads nothing. Run it when you want numbers.
        """;
}
