namespace LocalNEXUS.App.Nodes;

/// <summary>Where a model node sends its requests.</summary>
public enum ModelProvider
{
    /// <summary>A GGUF file served by a llama-server process started by this application.</summary>
    Local,

    /// <summary>A hosted model reached through OpenRouter.</summary>
    OpenRouter
}
