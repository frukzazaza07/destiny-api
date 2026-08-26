using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("")]
public sealed class SystemController(TarotMetrics metrics) : MasterController
{
    [HttpGet("health")]
    [ProducesResponseType(typeof(ResponseDto<HealthStatusDto, object>), StatusCodes.Status200OK)]
    public IActionResult Health() =>
        SuccessResponse(new HealthStatusDto("ok", "tarot-destiny-api"));

    [HttpGet("metrics")]
    [ProducesResponseType(typeof(ResponseDto<TarotMetricSnapshot, object>), StatusCodes.Status200OK)]
    public IActionResult Metrics() => SuccessResponse(metrics.Snapshot());
}
