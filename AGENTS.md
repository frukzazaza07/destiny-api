# Repository Instructions

## HTTP API documentation

- Include a generated OpenAPI document and an interactive Swagger UI in the initial implementation of every new HTTP API.
- Expose Swagger in Development by default. Do not expose interactive API documentation in Production unless the deployment explicitly requires it and access is appropriately secured.
- Keep Swagger request and response schemas aligned with endpoint behavior, and verify both the Swagger UI and generated OpenAPI JSON after API changes.

## Environment file security

- Never read, display, log, or copy values from `.env` files or other environment files that may contain secrets.
- When environment configuration must be inspected, read only the variable names (keys) and use `.env.example` for documented example values.
