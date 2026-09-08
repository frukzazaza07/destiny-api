using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;
using Microsoft.EntityFrameworkCore;
using TarotDestiny.Api.Data;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("internal/reading-jobs")]
[ProducesResponseType(typeof(ResponseDto<object, string>), 403)]
[ProducesResponseType(typeof(ResponseDto<object, object>), 400)]
public sealed class ReadingWorkerController(IOptions<ReadingJobOptions> options) : MasterController
{
    private ReadingJobService AuthorizedJobs()
    {
        var actual = Encoding.UTF8.GetBytes(Request.Headers["X-Worker-Key"].ToString());
        var expected = Encoding.UTF8.GetBytes(options.Value.WorkerKey);
        if (!options.Value.Enabled || expected.Length < 32 || !CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new ReadingJobException(403, "WORKER_ACCESS_DENIED");
        return HttpContext.RequestServices.GetRequiredService<ReadingJobService>();
    }
    [HttpPost("{id:guid}/claim")]
    [ProducesResponseType(typeof(ResponseDto<WorkerClaimDto, object>), 200)]
    public async Task<IActionResult> Claim(Guid id, WorkerAttemptDto request, CancellationToken ct) =>
        SuccessResponse(await AuthorizedJobs().Claim(id, request.AttemptId, false, ct));
    [HttpPost("{id:guid}/renew")]
    [ProducesResponseType(typeof(ResponseDto<WorkerClaimDto, object>), 200)]
    public async Task<IActionResult> Renew(Guid id, WorkerAttemptDto request, CancellationToken ct) =>
        SuccessResponse(await AuthorizedJobs().Claim(id, request.AttemptId, true, ct));

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(ResponseDto<ReadingJobMetricsDto, object>), 200)]
    public async Task<IActionResult> Metrics(CancellationToken ct)
    {
        AuthorizedJobs();
        var db = HttpContext.RequestServices.GetRequiredService<TarotDbContext>();
        var now = DateTimeOffset.UtcNow;
        var jobs = db.Set<ReadingJobEntity>();
        var oldest = await jobs.Where(x => x.State == "QUEUED").Select(x => (DateTimeOffset?)x.CreatedAt).MinAsync(ct);
        var recent = db.Set<ProviderAdmissionEntity>().Where(x => x.CreatedAt > now.AddMinutes(-1));
        return SuccessResponse(new ReadingJobMetricsDto(
            await jobs.CountAsync(x => x.State == "QUEUED", ct), await jobs.CountAsync(x => x.State == "RUNNING", ct),
            oldest is null ? 0 : (now - oldest.Value).TotalSeconds, await recent.CountAsync(ct),
            await recent.SumAsync(x => x.Tokens, ct), await db.Set<ReadingOutboxEntity>().CountAsync(x => x.PublishedAt == null, ct),
            await jobs.CountAsync(x => x.State == "FAILED", ct), await jobs.CountAsync(x => x.State == "CANCELED", ct)));
    }
}
