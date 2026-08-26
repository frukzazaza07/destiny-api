using Microsoft.AspNetCore.Mvc;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Controllers;

public abstract class MasterController : ControllerBase
{
    [NonAction]
    protected IActionResult SuccessResponse<T>(T data) =>
        Ok(new ResponseDto<T, object>(data, null));

    [NonAction]
    protected IActionResult ErrorResponse<T>(T errorData, ResponseCode code) =>
        StatusCode(
            (int)code,
            new ResponseDto<object, T>(null, errorData, code));
}
