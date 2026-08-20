namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// How a model is asked to express a change to a file.
/// </summary>
/// <remarks>
/// The format is not a detail. The same model scores very differently depending on which one it
/// is asked for, and the ordering is not the same for every model: search and replace blocks suit
/// large models, while a line tagged diff scores best for the smaller ones. The models this runs
/// on locally are the smaller ones, so the diff default is the line tagged form.
///
/// It is per model node because the right answer depends on the model behind that node, and one
/// graph can have a large hosted planner beside a small local coder.
/// </remarks>
public enum EditFormat
{
    /// <summary>
    /// Whole file for a new file or a small one, a line tagged diff for changes to larger files.
    /// The default, because rewriting a two hundred line file to change one method wastes most of
    /// a small context window on lines that were never in question.
    /// </summary>
    Automatic,

    /// <summary>Always return the complete file. Simplest, and the most tokens.</summary>
    WholeFile,

    /// <summary>Always return a line tagged diff, even for a new file.</summary>
    LineTaggedDiff
}
