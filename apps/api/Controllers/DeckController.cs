using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/deck")]
public sealed class DeckController(IDeckService deckService) : MasterController
{
    [HttpPost("shuffle")]
    [ProducesResponseType(typeof(ResponseDto<ShuffleDeckResponse, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, ValidationErrorsDto>), StatusCodes.Status400BadRequest)]
    public IActionResult Shuffle([FromBody] ShuffleDeckDto request)
    {
        var spread = string.IsNullOrWhiteSpace(request.Spread)
            ? SpreadIds.Destiny3
            : request.Spread;

        return SuccessResponse(deckService.CreateSession(spread));
    }

    [HttpPost("{sessionId}/resolve")]
    [ProducesResponseType(typeof(ResponseDto<ResolveDeckResponse, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ResponseDto<object, ApiErrorDto>), StatusCodes.Status404NotFound)]
    public IActionResult Resolve(
        [FromRoute] string sessionId,
        [FromBody] ResolveDeckDto request)
    {
        var result = deckService.Resolve(sessionId, request.SelectedIndexes!);
        return result is null
            ? ErrorResponse(
                new ApiErrorDto("Deck session not found or expired."),
                ResponseCode.NOT_FOUND)
            : SuccessResponse(result);
    }
}
