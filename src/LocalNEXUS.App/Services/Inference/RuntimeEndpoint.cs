namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Where a runtime put a model, and the id it answers to there.
/// </summary>
/// <remarks>
/// The model id is part of the answer rather than something the caller works out, because
/// runtimes disagree about it. llama-server accepts anything; the Python server refuses any id
/// but the exact path it was pinned to. A caller that guessed would be right for one runtime and
/// wrong for the other, so the runtime says.
/// </remarks>
/// <param name="BaseUrl">Root of the OpenAI compatible API, with no trailing path.</param>
/// <param name="ModelId">What to put in the request's model field.</param>
public sealed record RuntimeEndpoint(string BaseUrl, string ModelId);
