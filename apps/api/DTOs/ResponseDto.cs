using TarotDestiny.Api.Domain;

namespace TarotDestiny.Api.DTOs;

public sealed class ResponseDto<T, TError>
{
    public bool Success { get; }
    public T? Data { get; }
    public TError? Error { get; }
    public ResponseCode Code { get; }

    public ResponseDto(T? data, TError? error, ResponseCode code = ResponseCode.SUCCESS)
    {
        Code = code;
        Success = code == ResponseCode.SUCCESS;
        Data = data;
        Error = error;
    }
}
