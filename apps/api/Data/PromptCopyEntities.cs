using Microsoft.EntityFrameworkCore;

namespace TarotDestiny.Api.Data;

public sealed class PromptReadingEntity
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = "";
    public string ProtectedSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public bool Completed { get; set; }
    public int? RequiredAds { get; set; }
    public int CompletedAds { get; set; }
    public int Revision { get; set; }
}

public sealed class PromptAdAttemptEntity
{
    public Guid Id { get; set; }
    public Guid ReadingId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? ProviderEventId { get; set; }
    public bool Closed { get; set; }
}

public static class PromptCopyDataConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PromptReadingEntity>(e => {
            e.ToTable("prompt_readings"); e.HasKey(x => x.Id);
            e.Property(x => x.Owner).HasMaxLength(100);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasIndex(x => x.Owner);
        });
        model.Entity<PromptAdAttemptEntity>(e => {
            e.ToTable("prompt_ad_attempts"); e.HasKey(x => x.Id);
            e.Property(x => x.ProviderEventId).HasMaxLength(200);
            e.HasIndex(x => x.ProviderEventId).IsUnique();
            e.HasOne<PromptReadingEntity>().WithMany().HasForeignKey(x => x.ReadingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
