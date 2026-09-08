using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TarotDestiny.Api.Services;

public sealed class ReadingJobOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
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
