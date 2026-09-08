using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;
using Microsoft.Extensions.Options;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/readings")]
public sealed class ReadingController : MasterController
{
    private readonly ITarotReadingService _readingService;
    private readonly IDeepReadingAccessPolicy _accessPolicy;
    private readonly IRewardedDeepService _rewards;
    private readonly LlmOptions _llmOptions;

    public ReadingController(
        ITarotReadingService readingService,
        IDeepReadingAccessPolicy accessPolicy,
        IRewardedDeepService rewards,
        IOptions<LlmOptions> llmOptions)
    {
        _readingService = readingService;
        _accessPolicy = accessPolicy;
        _rewards = rewards;
        _llmOptions = llmOptions.Value;
    }

    [HttpGet("options")]
    [ProducesResponseType(typeof(ResponseDto<ReadingOptionsDto, object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOptions(CancellationToken cancellationToken)
    {
        var jobs = HttpContext.RequestServices.GetRequiredService<IOptions<ReadingJobOptions>>().Value;
        var deep = _accessPolicy.Evaluate(User);
        if (jobs.Enabled && !jobs.AllowCloudForRequestsWithRawQuestion) deep = deep with { Enabled = false };
        var rewardStatus = await _rewards.GetStatusAsync(User, RewardedDeepCookie.Read(Request), cancellationToken);
        deep = deep with
        {
            Entitled = deep.Entitled || rewardStatus.AvailableDeepCredits > 0,
            AvailableAdEarnedCredits = rewardStatus.AvailableDeepCredits
        };
        return SuccessResponse(new ReadingOptionsDto(
            [ReadingMode.STANDARD, ReadingMode.DEEP],
            deep,
            jobs.Enabled ? [new ReadingModelTierDto("CLOUD", jobs.Model, true)] : (_llmOptions.Tiers ?? [])
                .Select(tier => new ReadingModelTierDto(
                    tier.Id,
                    tier.Model,
                    tier.Workers.Any(worker => worker.Enabled)))
                .ToArray()));
    }

    [HttpPost("generate")]
    [ProducesResponseType(typeof(ResponseDto<TarotReadingResponse, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, ApiErrorDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ResponseDto<object, object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Generate(
        [FromBody] TarotReadingDto request,
        CancellationToken cancellationToken)
    {
        DeepCreditReservation? rewardReservation = null;
        if (request.ReadingMode == ReadingMode.DEEP)
        {
            if (HttpContext.RequestServices.GetRequiredService<IOptions<ReadingJobOptions>>().Value.Enabled)
                throw new ReadingJobException(409, "USE_READING_JOBS");
            var access = _accessPolicy.Evaluate(User);
            if (!access.Entitled)
            {
                rewardReservation = await _rewards.ReserveCreditAsync(User, RewardedDeepCookie.Read(Request), cancellationToken);
                if (rewardReservation is null)
                {
                    return ErrorResponse(
                        new ApiErrorDto(
                            "Deep readings require active premium access or one ad-earned DEEP credit.",
                            access.UpgradeUrl),
                        ResponseCode.FORBIDDEN);
                }
            }
        }

        try
        {
            var response = await _readingService.GenerateAsync(request, cancellationToken);
            if (rewardReservation is not null)
                await _rewards.FinalizeCreditAsync(rewardReservation, cancellationToken);
            return SuccessResponse(response);
        }
        catch
        {
            if (rewardReservation is not null)
            {
                if (cancellationToken.IsCancellationRequested)
                    await _rewards.FinalizeCreditAsync(rewardReservation, CancellationToken.None);
                else
                    await _rewards.ReleaseCreditAsync(rewardReservation, CancellationToken.None);
            }
            throw;
        }
    }
}
