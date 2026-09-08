using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TarotDestiny.Api.Data;
using TarotDestiny.Api.DTOs;

namespace TarotDestiny.Api.Services;

public sealed class ReadingJobExpiry(IServiceScopeFactory scopes, IOptions<ReadingJobOptions> options,
    ILogger<ReadingJobExpiry> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ReadingJobService>().Expire(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { logger.LogWarning("Job expiry temporarily unavailable: {Type}", ex.GetType().Name); }
            await Task.Delay(1000, ct);
        }
    }
}

public sealed class ReadingJobBroker(IServiceScopeFactory scopes, IOptions<ReadingJobOptions> options,
    ILogger<ReadingJobBroker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory { Uri = new Uri(options.Value.BrokerUri), AutomaticRecoveryEnabled = false };
                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), ct);
                await channel.QueueDeclareAsync("tarot.dead.v1", true, false, false, cancellationToken: ct);
                foreach (var queue in new[] { "tarot.requests.v1", "tarot.results.v1" })
                    await channel.QueueDeclareAsync(queue, true, false, false, new Dictionary<string, object?> {
                        ["x-dead-letter-exchange"] = "", ["x-dead-letter-routing-key"] = "tarot.dead.v1",
                        ["x-message-ttl"] = 300000
                    }, cancellationToken: ct);
                await channel.ExchangeDeclareAsync("tarot.cancellations.v1", ExchangeType.Fanout, true, cancellationToken: ct);
                var nextObservation = DateTimeOffset.MinValue;
                while (!ct.IsCancellationRequested)
                {
                    if (DateTimeOffset.UtcNow >= nextObservation)
                    {
                        var requests = await channel.QueueDeclarePassiveAsync("tarot.requests.v1", ct);
                        var dead = await channel.QueueDeclarePassiveAsync("tarot.dead.v1", ct);
                        logger.LogInformation("Reading broker queues: requests {QueueDepth}, dead letters {DeadLetters}", requests.MessageCount, dead.MessageCount);
                        nextObservation = DateTimeOffset.UtcNow.AddSeconds(30);
                    }
                    await PublishOutbox(channel, ct);
                    // Bounded pull consumption: one unacknowledged result per API replica.
                    var delivery = await channel.BasicGetAsync("tarot.results.v1", false, ct);
                    if (delivery is not null)
                    {
                        try
                        {
                            if (delivery.Body.Length > 300000) throw new JsonException();
                            var result = JsonSerializer.Deserialize<ReadingResultMessage>(delivery.Body.Span, ReadingJobService.Json) ?? throw new JsonException();
                            await using var scope = scopes.CreateAsyncScope();
                            var accepted = await scope.ServiceProvider.GetRequiredService<ReadingJobService>().Result(result, ct);
                            logger.LogInformation("Job result {JobId}/{AttemptId}: {Disposition}", result.JobId, result.AttemptId, accepted ? "committed" : "discarded");
                            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
                        }
                        catch (Exception ex) when (ex is JsonException or ReadingJobException)
                        { await channel.BasicRejectAsync(delivery.DeliveryTag, false, ct); }
                        // Database outages close the channel; broker redelivers without another provider call.
                    }
                    else await Task.Delay(250, ct);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Job broker reconnecting: {Type}", ex.GetType().Name);
                await Task.Delay(Random.Shared.Next(2000, 5000), ct);
            }
        }
    }
    private async Task PublishOutbox(IChannel channel, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TarotDbContext>();
        var pending = await db.Set<ReadingOutboxEntity>().Where(x => x.PublishedAt == null).OrderBy(x => x.CreatedAt).Take(20).ToListAsync(ct);
        foreach (var item in pending)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var cancel = item.Queue == "tarot.cancellations.v1";
            await channel.BasicPublishAsync(cancel ? item.Queue : "", cancel ? "" : item.Queue, !cancel,
                new BasicProperties { Persistent = true, ContentType = "application/json", MessageId = item.Id.ToString() },
                Encoding.UTF8.GetBytes(item.Body), timeout.Token);
            item.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
