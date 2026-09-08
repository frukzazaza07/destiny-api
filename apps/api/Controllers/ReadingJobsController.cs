using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/reading-jobs")]
[ProducesResponseType(typeof(ResponseDto<object, object>), 400)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 403)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 404)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 409)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 429)]
[ProducesResponseType(typeof(ResponseDto<object, string>), 503)]
public sealed class ReadingJobsController(IOptions<ReadingJobOptions> options) : MasterController
{
    private ReadingJobService Jobs => options.Value.Enabled
        ? HttpContext.RequestServices.GetRequiredService<ReadingJobService>()
        : throw new ReadingJobException(503, "QUEUED_DEEP_UNAVAILABLE");
    private string? Owner => ReadingJobService.Owner(User, RewardedDeepCookie.Read(Request));

    [HttpPost]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobDto, object>), 202)]
    public async Task<IActionResult> Create(CreateReadingJobDto request, CancellationToken ct)
    {
        var result = await Jobs.Create(request, User, RewardedDeepCookie.Read(Request), ct);
        return Accepted($"/api/reading-jobs/{result.JobId}", new ResponseDto<ReadingJobDto, object>(result, null));
    }
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobDto, object>), 200)]
    public async Task<IActionResult> Status(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return SuccessResponse(await Jobs.Get(id, Owner, ct));
    }
    [HttpPost("{id:guid}/cancel")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobDto, object>), 200)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => SuccessResponse(await Jobs.Cancel(id, Owner, ct));

    [HttpPost("{id:guid}/heartbeat")]
    [ApiAntiforgery]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobDto, object>), 200)]
    public async Task<IActionResult> Heartbeat(Guid id, ReadingHeartbeatDto request, CancellationToken ct) =>
        SuccessResponse(await Jobs.Presence(id, Owner, request.SubscriberId, true, ct, requireExisting: true));

    /// <summary>SSE status snapshots in ResponseDto envelopes. Event IDs increase with durable transitions.
    /// Reconnect reconciles the current persisted snapshot; intermediate events need not replay.
    /// Each stream renews its own presence lease. Last disconnect gets a 15 second default grace period.</summary>
    [HttpGet("{id:guid}/events")]
    [Produces("text/event-stream")]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobDto, object>), 200)]
    public async Task Events(Guid id, CancellationToken ct)
    {
        var jobs = Jobs;
        var owner = Owner;
        var subscriber = Guid.NewGuid();
        var snapshot = await jobs.Presence(id, owner, subscriber, true, ct);
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";
        long last = -1;
        try
        {
            await Response.WriteAsync($"event: presence\ndata: {JsonSerializer.Serialize(new ResponseDto<ReadingPresenceDto, object>(new(subscriber, options.Value.HeartbeatSeconds), null), ReadingJobService.Json)}\n\n", ct);
            while (!ct.IsCancellationRequested)
            {
                if (snapshot.EventId != last)
                {
                    await Response.WriteAsync($"id: {snapshot.EventId}\nevent: status\ndata: {JsonSerializer.Serialize(new ResponseDto<ReadingJobDto, object>(snapshot, null), ReadingJobService.Json)}\n\n", ct);
                    last = snapshot.EventId;
                }
                else await Response.WriteAsync(": keepalive\n\n", ct);
                await Response.Body.FlushAsync(ct);
                if (ReadingJobService.Terminal(snapshot.State)) break;
                await Task.Delay(TimeSpan.FromSeconds(options.Value.HeartbeatSeconds), ct);
                // PostgreSQL reconciliation also delivers results committed by another API replica.
                snapshot = await jobs.Get(id, owner, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await jobs.Presence(id, owner, subscriber, false, cleanup.Token); }
            catch { /* Persisted lease expiry handles process loss and database outages. */ }
        }
    }
}
