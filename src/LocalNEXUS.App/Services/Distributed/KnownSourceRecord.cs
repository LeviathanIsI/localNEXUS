namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The persisted form of a registered source. A plain shape for the configuration file; the
/// registry hydrates it into an <see cref="InferenceSource"/> at startup. The registry does
/// not know or care how entries arrived here, which is the seam a discovery mechanism will
/// plug into later.
/// </summary>
public sealed class KnownSourceRecord
{
    /// <summary>The source's stable identity.</summary>
    public Guid SourceId { get; set; }

    /// <summary>Human label.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Host name or address of the source's rpc-server.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Port of the source's rpc-server.</summary>
    public int Port { get; set; }

    /// <summary>Stored as the enum name so the file stays readable and editable.</summary>
    public string Locality { get; set; } = nameof(SourceLocality.LocalNetwork);

    /// <summary>Declared memory in MiB. Zero means unknown.</summary>
    public long MemoryMb { get; set; }
}
