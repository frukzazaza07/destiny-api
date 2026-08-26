using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/readings")]
public sealed class ReadingController : MasterController
{
    private readonly ITarotReadingService _readingService;
    private readonly IDeepReadingAccessPolicy _accessPolicy;

    public ReadingController(
        ITarotReadingService readingService,
        IDeepReadingAccessPolicy accessPolicy)
    {
        _readingService = readingService;
        _accessPolicy = accessPolicy;
    }

    [HttpGet("options")]
    [ProducesResponseType(typeof(ResponseDto<ReadingOptionsDto, object>), StatusCodes.Status200OK)]
    public IActionResult GetOptions()
    {
        var deep = _accessPolicy.Evaluate(User);
        return SuccessResponse(new ReadingOptionsDto(
            [ReadingMode.STANDARD, ReadingMode.DEEP],
            deep));
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
        if (request.ReadingMode == ReadingMode.DEEP)
        {
            var access = _accessPolicy.Evaluate(User);
            if (!access.Entitled)
            {
                return ErrorResponse(
                    new ApiErrorDto(
                        "Deep readings require an active premium entitlement.",
                        access.UpgradeUrl),
                    ResponseCode.FORBIDDEN);
            }
        }

        var response = await _readingService.GenerateAsync(request, cancellationToken);
        return SuccessResponse(response);
    }
}
