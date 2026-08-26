# Repository Instructions

## HTTP API documentation

- Include a generated OpenAPI document and an interactive Swagger UI in the initial implementation of every new HTTP API.
- Expose Swagger in Development by default. Do not expose interactive API documentation in Production unless the deployment explicitly requires it and access is appropriately secured.
- Keep Swagger request and response schemas aligned with endpoint behavior, and verify both the Swagger UI and generated OpenAPI JSON after API changes.
