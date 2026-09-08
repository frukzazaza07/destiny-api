using Microsoft.EntityFrameworkCore;

namespace TarotDestiny.Api.Data;

public sealed class ReadingJobEntity
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string RequestJson { get; set; } = "";
    public string ProviderRef { get; set; } = "";
    public string State { get; set; } = "QUEUED";
    public Guid AttemptId { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public DateTimeOffset PresenceUntil { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? CreditId { get; set; }
    public Guid? ReservationId { get; set; }
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
}
public sealed class ReadingPresenceEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
public sealed class ReadingOutboxEntity
{
    public Guid Id { get; set; }
    public string Queue { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
public sealed class ProviderAdmissionEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Tokens { get; set; }
}
public static class ReadingJobDataConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ReadingJobEntity>(e => {
            e.ToTable("reading_jobs"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Owner, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.State, x.Deadline });
            e.Property(x => x.Owner).HasMaxLength(100);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Revision).IsConcurrencyToken();
        });
        model.Entity<ReadingPresenceEntity>(e => {
            e.ToTable("reading_presence"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.ExpiresAt });
            e.HasOne<ReadingJobEntity>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<ReadingOutboxEntity>(e => {
            e.ToTable("reading_outbox"); e.HasKey(x => x.Id); e.HasIndex(x => x.PublishedAt);
        });
        model.Entity<ProviderAdmissionEntity>(e => {
            e.ToTable("provider_admissions"); e.HasKey(x => x.Id); e.HasIndex(x => x.CreatedAt);
        });
    }
}
