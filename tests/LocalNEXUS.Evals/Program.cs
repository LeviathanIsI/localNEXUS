using System.IO;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Evals;

// The harness runs deliberately, from a command line, and produces numbers rather than a verdict.
// It is not a gate, it is not in any build, and nothing in it fails on a threshold: a model is not
// deterministic, and a task that comes out right four times in five is ordinary.

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(EvalOptions.Usage);
    return 0;
}

if (!EvalOptions.TryParse(args, out var options, out var argumentError))
{
    Console.Error.WriteLine(argumentError);
    Console.Error.WriteLine();
    Console.Error.WriteLine(EvalOptions.Usage);
    return 2;
}

if (AppPaths.FindLlamaServerExecutable() is null)
{
    Console.Error.WriteLine(
        "llama-server was not found. It is expected in vendor/llama beside the repository or beside "
        + "the published exe. Nothing here downloads it.");

    return 2;
}

var models = options.ResolveModels(out var unmatched);

foreach (var name in unmatched)
{
    Console.Error.WriteLine($"No model on disk matches '{name}'.");
}

if (models.Count == 0)
{
    Console.Error.WriteLine(
        $"No model to run. Put a GGUF under {AppPaths.Models} or name one with --models. "
        + "Nothing here downloads a model.");

    return 2;
}

if (options.DebateOnly)
{
    using var probe = new DebateProbe(options);

    await probe.RunAsync(
        models[0],
        Path.Combine(options.OutputDirectory, "debate-transcript.md"),
        CancellationToken.None);

    return 0;
}

var tasks = TaskSet.Select(options.Tasks);

if (options.DiagnoseOnly)
{
    using var planner = new PlannerProbe(options);

    await planner.RunAsync(
        models[0],
        tasks,
        Path.Combine(options.OutputDirectory, "planner-diagnosis.md"),
        CancellationToken.None);

    return 0;
}

if (tasks.Count == 0)
{
    Console.Error.WriteLine("No task matched. Known tasks: " + string.Join(", ", TaskSet.Tasks.Select(t => t.Id)));
    return 2;
}

using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    // One press asks the run to stop and lets the harness bring the server down; a second is the
    // usual way out.
    e.Cancel = !stopping.IsCancellationRequested;
    stopping.Cancel();
};

Console.WriteLine($"Task set v{TaskSet.Version}, {tasks.Count} task(s), {options.Repeats} repeat(s) each.");
Console.WriteLine($"Results go to {options.OutputDirectory}");
Console.WriteLine();

foreach (var model in models)
{
    Console.WriteLine($"{Path.GetFileName(model)}");

    var harness = new EvalHarness(options);

    try
    {
        var run = await harness.RunAsync(model, tasks, stopping.Token);
        var folder = ResultWriter.Write(run, tasks, options.OutputDirectory);

        Console.WriteLine();
        Console.WriteLine(ResultWriter.Summarise(run, tasks));
        Console.WriteLine($"Written to {folder}");
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Stopped. Nothing was written for this model.");
        return 1;
    }
    finally
    {
        harness.Dispose();
    }

    Console.WriteLine();
}

return 0;
