namespace TarotDestiny.Api.Domain;

public enum ResponseCode
{
    SUCCESS = 200,
    INVALID_REQUEST = 400,
    UNAUTHORIZED = 401,
    FORBIDDEN = 403,
    NOT_FOUND = 404,
    CONFLICT = 409,
    TOO_MANY_REQUESTS = 429,
    SERVICE_UNAVAILABLE = 503,
    INTERNAL_ERROR = 502
}
