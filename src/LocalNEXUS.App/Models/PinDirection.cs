namespace LocalNEXUS.App.Models;

/// <summary>Whether a pin consumes a value or produces one.</summary>
public enum PinDirection
{
    /// <summary>Consumes a value from an upstream node.</summary>
    Input,

    /// <summary>Produces a value for downstream nodes.</summary>
    Output
}
