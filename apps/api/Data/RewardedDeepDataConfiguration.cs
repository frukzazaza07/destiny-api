using Microsoft.EntityFrameworkCore;

namespace TarotDestiny.Api.Data;

internal static class RewardedDeepDataConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RewardedDeepSettingsEntity>(entity =>
        {
            entity.ToTable("rewarded_deep_settings", table =>
            {
                table.HasCheckConstraint("ck_rewarded_deep_settings_singleton", "id = 1");
                table.HasCheckConstraint("ck_rewarded_deep_settings_required", "required_ad_completions BETWEEN 1 AND 10");
                table.HasCheckConstraint("ck_rewarded_deep_settings_credits", "deep_credits_per_completed_bundle BETWEEN 1 AND 5");
                table.HasCheckConstraint("ck_rewarded_deep_settings_revision", "revision >= 0");
            });
            entity.HasKey(item => item.Id).HasName("pk_rewarded_deep_settings");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.RequiredAdCompletions).HasColumnName("required_ad_completions").IsRequired();
            entity.Property(item => item.DeepCreditsPerCompletedBundle).HasColumnName("deep_credits_per_completed_bundle").IsRequired();
            entity.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0).IsConcurrencyToken();
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.UpdatedByUserId).HasColumnName("updated_by_user_id");
            entity.HasData(new RewardedDeepSettingsEntity
            {
                Id = RewardedDeepConstants.SettingsId,
                RequiredAdCompletions = 3,
                DeepCreditsPerCompletedBundle = 1,
                Revision = 0,
                UpdatedAt = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)
            });
        });

        modelBuilder.Entity<RewardedDeepSessionEntity>(entity =>
        {
            entity.ToTable("rewarded_deep_session", table =>
            {
                table.HasCheckConstraint("ck_rewarded_deep_session_identity", "user_id IS NOT NULL OR anonymous_token_hash IS NOT NULL");
                table.HasCheckConstraint("ck_rewarded_deep_session_required", "required_ad_completions BETWEEN 1 AND 10");
                table.HasCheckConstraint("ck_rewarded_deep_session_credits", "deep_credits_per_completed_bundle BETWEEN 1 AND 5");
                table.HasCheckConstraint("ck_rewarded_deep_session_progress", "valid_ad_completions >= 0 AND valid_ad_completions <= required_ad_completions");
                table.HasCheckConstraint("ck_rewarded_deep_session_expiry", "expires_at > created_at");
            });
            entity.HasKey(item => item.Id).HasName("pk_rewarded_deep_session");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.AnonymousTokenHash).HasColumnName("anonymous_token_hash").HasMaxLength(64);
            entity.Property(item => item.RequiredAdCompletions).HasColumnName("required_ad_completions").IsRequired();
            entity.Property(item => item.DeepCreditsPerCompletedBundle).HasColumnName("deep_credits_per_completed_bundle").IsRequired();
            entity.Property(item => item.ValidAdCompletions).HasColumnName("valid_ad_completions").IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0).IsConcurrencyToken();
            entity.HasIndex(item => item.AnonymousTokenHash).IsUnique().HasDatabaseName("ux_rewarded_deep_session_anonymous_token");
            entity.HasIndex(item => new { item.UserId, item.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_rewarded_deep_session_user_created");
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RewardedAdAttemptEntity>(entity =>
        {
            entity.ToTable("rewarded_ad_attempt", table =>
                table.HasCheckConstraint("ck_rewarded_ad_attempt_expiry", "expires_at > created_at"));
            entity.HasKey(item => item.Id).HasName("pk_rewarded_ad_attempt");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.SessionId).HasColumnName("session_id");
            entity.Property(item => item.NonceHash).HasColumnName("nonce_hash").HasMaxLength(64).IsRequired();
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.UsedAt).HasColumnName("used_at").HasColumnType("timestamp with time zone").IsConcurrencyToken();
            entity.HasIndex(item => item.NonceHash).IsUnique().HasDatabaseName("ux_rewarded_ad_attempt_nonce");
            entity.HasIndex(item => new { item.SessionId, item.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_rewarded_ad_attempt_session_created");
            entity.HasOne(item => item.Session).WithMany(session => session.Attempts).HasForeignKey(item => item.SessionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RewardedDeepCreditEntity>(entity =>
        {
            entity.ToTable("rewarded_deep_credit", table =>
                table.HasCheckConstraint("ck_rewarded_deep_credit_expiry", "expires_at > issued_at"));
            entity.HasKey(item => item.Id).HasName("pk_rewarded_deep_credit");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.SessionId).HasColumnName("session_id");
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.IssuedAt).HasColumnName("issued_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ReservationId).HasColumnName("reservation_id");
            entity.Property(item => item.ReservedAt).HasColumnName("reserved_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.ReservationExpiresAt).HasColumnName("reservation_expires_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0).IsConcurrencyToken();
            entity.HasIndex(item => new { item.UserId, item.ExpiresAt }).HasDatabaseName("ix_rewarded_deep_credit_user_expiry");
            entity.HasIndex(item => new { item.SessionId, item.ExpiresAt }).HasDatabaseName("ix_rewarded_deep_credit_session_expiry");
            entity.HasIndex(item => item.ReservationId).IsUnique().HasDatabaseName("ux_rewarded_deep_credit_reservation");
            entity.HasOne(item => item.Session).WithMany(session => session.Credits).HasForeignKey(item => item.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
