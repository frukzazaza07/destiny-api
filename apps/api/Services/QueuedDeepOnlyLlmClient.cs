using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

// Prevent admin warmups or another synchronous caller from bypassing the durable job lifecycle.
public sealed class QueuedDeepOnlyLlmClient : ILlmClient
{
    public Task<TarotReadingResponse> GenerateAsync(TarotReadingDto request, ClassificationResult classification,
        InterpretationPayload payload, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("DEEP generation requires a durable reading job.");
}
