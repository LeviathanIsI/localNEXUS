namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Raised when an inference endpoint rejects a request or returns something that cannot be read
/// as a chat completion stream. The message is written straight into the activity feed, so it is
/// phrased for a person rather than for a log parser.
/// </summary>
public sealed class ModelClientException : Exception
{
    public ModelClientException(string message)
        : base(message)
    {
    }

    public ModelClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
