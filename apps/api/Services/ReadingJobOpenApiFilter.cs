using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TarotDestiny.Api.Services;

public sealed class ReadingJobOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath == "api/reading-jobs/thai-astrology")
            operation.Description = "Create a personalized THAI_ASTROLOGY reading with DEEP entitlement or one earned credit. " +
                "Requires CSRF and the account or reward-session cookie. Validates before reserving access. " +
                "Birth information and question require AllowCloudForRequestsWithRawQuestion. " +
                "Returns 202; recover, stream, heartbeat and cancel using the common reading-jobs routes. " +
                "COMPLETED returns astrologyReading with reading=null. Timing is null in NO_CHART_V1. " +
                "Idempotency keys are shared across reading types per owner; different input with the same key returns 409.";
        if (context.ApiDescription.RelativePath != "api/reading-jobs/{id}/events") return;
        operation.Description = "SSE event `status`: data is a ResponseDto<ReadingJobDto, object> JSON envelope. " +
            "The initial `presence` event contains a ResponseDto<ReadingPresenceDto, object> with subscriberId and heartbeatSeconds. " +
            "The browser must POST that subscriberId to the heartbeat endpoint at the specified cadence; server keepalives do not renew presence. " +
            "The `id` is the durable, increasing job revision. On reconnect the latest persisted snapshot is sent " +
            "regardless of Last-Event-ID; intermediate revisions need not replay. Comment keepalives default to every 5 seconds. " +
            "Each subscriber renews a 10-second presence lease, followed by a 15-second reconnect grace period. " +
            "Initial subscription must arrive within 15 seconds. Cancel POST transitions immediately; a committed completion survives cancellation. " +
            "Status and result access require the original account or reward-session cookie. Errors before streaming are JSON envelopes.";
        operation.Responses["200"].Content["text/event-stream"].Schema = new OpenApiSchema {
            OneOf = [context.SchemaGenerator.GenerateSchema(typeof(TarotDestiny.Api.DTOs.ResponseDto<TarotDestiny.Api.DTOs.ReadingJobDto, object>), context.SchemaRepository),
                context.SchemaGenerator.GenerateSchema(typeof(TarotDestiny.Api.DTOs.ResponseDto<TarotDestiny.Api.DTOs.ReadingPresenceDto, object>), context.SchemaRepository)]
        };
        foreach (var response in operation.Responses.Where(x => x.Key != "200"))
        {
            var media = response.Value.Content.Values.FirstOrDefault();
            if (media is null) continue;
            response.Value.Content.Clear();
            response.Value.Content["application/json"] = media;
        }
    }
}

public sealed class ThaiAstrologySchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(TarotDestiny.Api.DTOs.ThaiAstrologyReadingDto)) return;
        schema.Properties["birthDate"].Format = "date";
        schema.Properties["birthDate"].Description = "Required real, non-future Gregorian YYYY-MM-DD date; never a UTC timestamp or Buddhist Era year.";
        schema.Properties["birthTime"].Nullable = true;
        schema.Properties["birthTime"].Description = "Optional local 24-hour HH:mm. 00:00 means midnight. Null, omitted or blank means unknown; no timezone is assumed.";
        schema.Properties["birthPlace"].Nullable = true;
        schema.Properties["birthPlace"].Description = "Optional location text, trimmed, at most 200 characters. Blank means missing. No geocoding or timezone resolution.";
        schema.Properties["question"].Description = "Required nonblank customer question, trimmed, at most 2000 characters.";
        schema.Properties["locale"].Nullable = true;
        schema.Properties["locale"].Description = "Explicit th or en controls all prose. Omitted/null resolves by dominant Thai/Latin script in the question; ties/other scripts default to th.";
    }
}
