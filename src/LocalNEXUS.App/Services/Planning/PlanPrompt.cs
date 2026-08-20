using System.Text;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Builds what the planner is asked, and what a coder is asked for one file of the plan.
/// </summary>
/// <remarks>
/// Kept apart from the nodes because the exact wording is the thing most likely to need changing,
/// and because both prompts have to agree about the shape of what comes back.
/// </remarks>
public static class PlanPrompt
{
    /// <summary>The system prompt a planning model runs under.</summary>
    public const string PlannerSystemPrompt =
        "You plan changes to an existing Unity project. You never write code. "
        + "You answer only in the two sections you are asked for, using the exact row format given, "
        + "with no commentary, no explanation and no markdown fences.";

    /// <summary>
    /// The planner's message: what exists, what was asked for, and the format of the answer.
    /// </summary>
    public static string BuildPlannerMessage(
        string request,
        string projectMap,
        string candidateSummary,
        ContextBudget budget)
    {
        var builder = new StringBuilder();

        builder.AppendLine("This Unity project already contains the following. Each line is a file and what it declares.");
        builder.AppendLine();
        builder.AppendLine(projectMap.Length == 0 ? "(the project has no C# files yet)" : projectMap);
        builder.AppendLine();

        if (candidateSummary.Length > 0)
        {
            builder.AppendLine("These files look closest to the request. Their members are listed so you can tell what they already do.");
            builder.AppendLine();
            builder.AppendLine(candidateSummary);
            builder.AppendLine();
        }

        builder.AppendLine("The request is:");
        builder.AppendLine(request.Trim());
        builder.AppendLine();
        builder.AppendLine("Answer in exactly two sections.");
        builder.AppendLine();
        builder.AppendLine("DECISIONS");
        builder.AppendLine("One row per file listed above that is relevant, in this format:");
        builder.AppendLine("path | USE_AS_IS or EDIT or CREATE_NEW_REFERENCING <TypeName> or IGNORE | why");
        builder.AppendLine();
        builder.AppendLine("PLAN");
        builder.AppendLine("One row per file to write, in the order they must be written, in this format:");
        builder.AppendLine("order | CREATE or EDIT | path | main type name | what this file is for");
        builder.AppendLine();
        builder.AppendLine("Rules.");
        builder.AppendLine("Order the plan by dependency: interfaces and data types first, then what implements them, then what uses them.");
        builder.AppendLine("Do not create a type that already exists above. Edit its file, or write something that references it.");
        builder.AppendLine("A MonoBehaviour file name must match its class name exactly.");
        builder.AppendLine("Write as many files as the request genuinely needs, and no more.");

        return ContextBudget.Fit(builder.ToString(), budget.TotalCharacters, "the planning prompt");
    }

    /// <summary>
    /// The message a coder is given for one file of the plan, including what earlier files in the
    /// same run defined.
    /// </summary>
    public static string BuildCoderMessage(CodeTask task, string emittedSignatures, bool wholeFile)
    {
        var builder = new StringBuilder();

        builder.AppendLine(task.Operation == FileOperation.Create
            ? $"Write a new file {task.RelativePath} declaring {task.TypeName}."
            : $"Change the existing file {task.RelativePath}.");

        builder.AppendLine();
        builder.AppendLine("What this file is for:");
        builder.AppendLine(task.Intent.Length == 0 ? "(no further detail was given)" : task.Intent);
        builder.AppendLine();

        if (task.ProjectContext.Length > 0)
        {
            builder.AppendLine("What already exists in the project that this must fit into:");
            builder.AppendLine();
            builder.AppendLine(task.ProjectContext);
            builder.AppendLine();
        }

        if (emittedSignatures.Length > 0)
        {
            builder.AppendLine("Written earlier in this same request, so use these exactly as they are:");
            builder.AppendLine();
            builder.AppendLine(emittedSignatures);
            builder.AppendLine();
        }

        if (task.ExistingContent is { Length: > 0 })
        {
            builder.AppendLine($"The current content of {task.RelativePath}:");
            builder.AppendLine();
            builder.AppendLine(task.ExistingContent);
            builder.AppendLine();
        }

        builder.Append(wholeFile
            ? "Return the complete file. Output raw C# only: no markdown fences, no commentary."
            : EditFormatInstruction(task));

        return builder.ToString();
    }

    /// <summary>
    /// The instruction for a diff shaped reply. Line tagged, because that is the format the
    /// research finds smaller models handle best, and because a tagged line can be matched even
    /// when its indentation comes back slightly wrong.
    /// </summary>
    private static string EditFormatInstruction(CodeTask task)
        => $"Return only the changes to {task.RelativePath}, as one or more blocks in exactly this format:"
           + Environment.NewLine
           + Environment.NewLine
           + "@@" + Environment.NewLine
           + "-lines to remove, each prefixed with a minus" + Environment.NewLine
           + "+lines to add, each prefixed with a plus" + Environment.NewLine
           + " lines that stay, each prefixed with a space" + Environment.NewLine
           + Environment.NewLine
           + "Include at least two unchanged lines above and below each change so it can be located. "
           + "Do not output the whole file, no markdown fences, no commentary.";
}
