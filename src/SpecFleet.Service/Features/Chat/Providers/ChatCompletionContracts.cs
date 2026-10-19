namespace SpecFleet.Service.Features.Chat.Providers;

public sealed record ChatCompletionRequest(
    string Prompt,
    string? SystemInstruction);

public sealed record ChatCompletionChunk(string Text);

public sealed record ChatCompletionResult(
    string Content,
    string Model,
    bool WasCancelled,
    string? Error);
