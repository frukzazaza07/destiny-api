# API Project Instructions

## Endpoint structure

- Implement HTTP endpoints in controller classes under `Controllers/`; do not add route handlers directly to `Program.cs`.
- Keep `Program.cs` focused on configuration, dependency registration, and middleware/controller mapping.
- Put endpoint request models in `DTOs/` and give responses explicit public types; do not expose persistence models or anonymous error shapes as HTTP contracts.

## Responses and errors

- Return every API success and error through the shared `ResponseDto` envelope.
- Add DataAnnotations validation attributes to request DTOs and rely on `[ApiController]` plus the global invalid-model-state response for request validation.
- Let unhandled exceptions flow through `GlobalExceptionHandler`; do not duplicate broad `try`/`catch` blocks in controllers.
- Translate expected domain failures to the appropriate HTTP status and shared error envelope without exposing stack traces or internal exception details.

## API documentation and verification

- Keep controller response metadata and Swagger/OpenAPI schemas aligned with the actual envelope and status codes.
- Add or update tests for new routes, DTO validation, response envelopes, and error mappings.
