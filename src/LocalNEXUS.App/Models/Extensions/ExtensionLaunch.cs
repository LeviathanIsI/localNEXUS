using System.Text.Json.Serialization;

namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// Exactly how the host starts the extension process.
/// </summary>
/// <param name="Command">The executable or launcher to run.</param>
/// <param name="Arguments">Arguments, already split, so nothing has to guess at quoting.</param>
/// <param name="WorkingDirectory">Working directory, or null to use the extension folder.</param>
/// <param name="Environment">Extra environment variables, added to the ones this process has.</param>
/// <remarks>
/// Arguments are a list rather than a single string on purpose. A command line assembled by
/// concatenation is the classic way a path containing a space becomes two broken arguments, and
/// extension authors write paths containing spaces.
/// <para>
/// This is shown verbatim in the details pane. A misconfigured extension is diagnosable in one
/// glance when the actual command is on screen, and a guessing game when it is not.
/// </para>
/// </remarks>
public sealed record ExtensionLaunch(
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    /// <summary>The command and its arguments as a person would type them, for display.</summary>
    [JsonIgnore]
    public string DisplayCommand
    {
        get
        {
            var parts = new List<string> { Quote(Command) };
            parts.AddRange(Arguments.Select(Quote));
            return string.Join(' ', parts);
        }
    }

    private static string Quote(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
}
