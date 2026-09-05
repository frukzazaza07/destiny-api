using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Controllers;

[ApiController]
[Route("api/classifier")]
public sealed class ClassifierTrainingController(
    IClassifierTrainingStore store,
    IAdminAccessPolicy adminAccess) : MasterController
{
    [HttpPost("training-examples")]
    [ProducesResponseType(typeof(ResponseDto<ClassifierTrainingSubmissionResultDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit(
        [FromBody] ClassifierTrainingSubmissionDto request,
        CancellationToken cancellationToken) =>
        SuccessResponse(await store.SubmitAsync(request, cancellationToken));

    [HttpGet("training-examples")]
    [ProducesResponseType(typeof(ResponseDto<ClassifierTrainingPageDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(
        [FromQuery] string? status = "PENDING",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        return SuccessResponse(await store.ListAsync(status, page, pageSize, cancellationToken));
    }

    [HttpPut("training-examples/{id:guid}/review")]
    [ApiAntiforgeryForCookieUser]
    [ProducesResponseType(typeof(ResponseDto<ClassifierTrainingExampleDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Review(
        Guid id,
        [FromBody] ClassifierReviewDto review,
        CancellationToken cancellationToken)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        var result = await store.ReviewAsync(id, review, cancellationToken);
        return result is null
            ? ErrorResponse("Classifier training example was not found.", ResponseCode.NOT_FOUND)
            : SuccessResponse(result);
    }

    [HttpGet("taxonomy")]
    [ProducesResponseType(typeof(ResponseDto<ClassifierTaxonomyDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public IActionResult Taxonomy()
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        var domains = Enum.GetValues<TarotDomain>()
            .Select(domain => new ClassifierTaxonomyItemDto(
                domain.ToString(),
                TarotIntents.All
                    .Where(intent => intent != TarotIntents.PersonalCustom &&
                        intent.StartsWith($"{domain}_", StringComparison.Ordinal))
                    .ToArray()))
            .ToArray();
        return SuccessResponse(new ClassifierTaxonomyDto(domains));
    }

    [HttpGet("training-examples/export")]
    [ProducesResponseType(typeof(ResponseDto<ReviewedClassifierExportDto, object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseDto<object, string>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        if (!adminAccess.IsAllowed(HttpContext))
        {
            return ErrorResponse("Administrator authentication is required.", ResponseCode.UNAUTHORIZED);
        }

        return SuccessResponse(await store.ExportApprovedAsync(cancellationToken));
    }
}
