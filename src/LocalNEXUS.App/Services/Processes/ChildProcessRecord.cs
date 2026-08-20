namespace LocalNEXUS.App.Services.Processes;

/// <summary>
/// One engine process this application started, recorded on disk so that a later session can
/// recognise it if this one never gets to clean it up.
/// </summary>
/// <remarks>
/// A process id alone is not an identity: Windows reuses them, and the user may be running an
/// engine of their own that this application must never touch. The start time pins the id to one
/// particular process and the executable path pins it to a binary this application launched, so
/// a record only matches when all three agree.
///
/// The owner fields carry the same facts about the application process that wrote the record.
/// A second copy of LocalNEXUS running at the same time therefore leaves the first one's
/// children alone, because their owner is still alive.
/// </remarks>
public sealed class ChildProcessRecord
{
    /// <summary>Process id of the engine process.</summary>
    public int Pid { get; set; }

    /// <summary>When that process started, which is what makes the id unambiguous.</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>The binary that was launched.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Which engine this is, for the log line rather than for logic.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Process id of the LocalNEXUS instance that started it.</summary>
    public int OwnerPid { get; set; }

    /// <summary>When that instance started.</summary>
    public DateTimeOffset OwnerStartedUtc { get; set; }
}
