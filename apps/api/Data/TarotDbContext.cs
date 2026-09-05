using Microsoft.EntityFrameworkCore;

namespace TarotDestiny.Api.Data;

public sealed class TarotDbContext(DbContextOptions<TarotDbContext> options) : DbContext(options)
{
    public DbSet<GeneratedAnswerEntity> GeneratedAnswers => Set<GeneratedAnswerEntity>();
    public DbSet<GeneratedAnswerVariantEntity> GeneratedAnswerVariants => Set<GeneratedAnswerVariantEntity>();
    public DbSet<ClassifierTrainingExampleEntity> ClassifierTrainingExamples => Set<ClassifierTrainingExampleEntity>();
    public DbSet<UserAccountEntity> Users => Set<UserAccountEntity>();
    public DbSet<AccountRoleEntity> Roles => Set<AccountRoleEntity>();
    public DbSet<UserAccountRoleEntity> UserRoles => Set<UserAccountRoleEntity>();
    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();
    public DbSet<UserAccountTokenEntity> AccountTokens => Set<UserAccountTokenEntity>();
    public DbSet<UserEntitlementEntity> UserEntitlements => Set<UserEntitlementEntity>();
    public DbSet<UserEntitlementAuditEntity> UserEntitlementAudits => Set<UserEntitlementAuditEntity>();
    public DbSet<RewardedDeepSettingsEntity> RewardedDeepSettings => Set<RewardedDeepSettingsEntity>();
    public DbSet<RewardedDeepSessionEntity> RewardedDeepSessions => Set<RewardedDeepSessionEntity>();
    public DbSet<RewardedAdAttemptEntity> RewardedAdAttempts => Set<RewardedAdAttemptEntity>();
    public DbSet<RewardedDeepCreditEntity> RewardedDeepCredits => Set<RewardedDeepCreditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AccountDataConfiguration.Configure(modelBuilder);
        RewardedDeepDataConfiguration.Configure(modelBuilder);

        modelBuilder.Entity<GeneratedAnswerEntity>(entity =>
        {
            entity.ToTable("tarot_generated_answer", table =>
            {
                table.HasCheckConstraint(
                    "ck_tarot_generated_answer_cache_hash",
                    "length(cache_hash) = 64");
                table.HasCheckConstraint(
                    "ck_tarot_generated_answer_hit_count",
                    "hit_count >= 0");
            });

            entity.HasKey(answer => answer.Id)
                .HasName("pk_tarot_generated_answer");

            entity.Property(answer => answer.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            entity.Property(answer => answer.CacheHash)
                .HasColumnName("cache_hash")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(answer => answer.CacheVersion)
                .HasColumnName("cache_version")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(answer => answer.Domain)
                .HasColumnName("domain")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(answer => answer.Intent)
                .HasColumnName("intent")
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(answer => answer.ReadingMode)
                .HasColumnName("reading_mode")
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(answer => answer.SpreadId)
                .HasColumnName("spread_id")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(answer => answer.Locale)
                .HasColumnName("locale")
                .HasMaxLength(10)
                .IsRequired();
            entity.Property(answer => answer.CardsJson)
                .HasColumnName("cards")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(answer => answer.PromptVersion)
                .HasColumnName("prompt_version")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(answer => answer.InterpretationVersion)
                .HasColumnName("interpretation_version")
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(answer => answer.ModelVersion)
                .HasColumnName("model_version")
                .HasMaxLength(100);
            entity.Property(answer => answer.HitCount)
                .HasColumnName("hit_count")
                .HasDefaultValue(0L)
                .IsRequired();
            entity.Property(answer => answer.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();
            entity.Property(answer => answer.UpdatedAt)
                .HasColumnName("updated_at")
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(answer => answer.CacheHash)
                .IsUnique()
                .HasDatabaseName("ux_tarot_generated_answer_cache_hash");
            entity.HasIndex(answer => new { answer.Domain, answer.Intent, answer.ReadingMode })
                .HasDatabaseName("ix_tarot_generated_answer_domain_intent_reading_mode");
            entity.HasIndex(answer => answer.HitCount)
                .IsDescending()
                .HasDatabaseName("ix_tarot_generated_answer_hit_count");
        });

        modelBuilder.Entity<GeneratedAnswerVariantEntity>(entity =>
        {
            entity.ToTable("tarot_generated_answer_variant", table =>
            {
                table.HasCheckConstraint(
                    "ck_tarot_generated_answer_variant_number",
                    "variant_number > 0");
            });

            entity.HasKey(variant => variant.Id)
                .HasName("pk_tarot_generated_answer_variant");

            entity.Property(variant => variant.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            entity.Property(variant => variant.GeneratedAnswerId)
                .HasColumnName("generated_answer_id")
                .IsRequired();
            entity.Property(variant => variant.VariantNumber)
                .HasColumnName("variant_number")
                .IsRequired();
            entity.Property(variant => variant.ResponseJson)
                .HasColumnName("response")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(variant => variant.CreatedAt)
                .HasColumnName("created_at")
                .HasColumnType("timestamp with time zone")
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .IsRequired();

            entity.HasIndex(variant => new { variant.GeneratedAnswerId, variant.VariantNumber })
                .IsUnique()
                .HasDatabaseName("ux_tarot_generated_answer_variant_answer_number");

            entity.HasOne(variant => variant.GeneratedAnswer)
                .WithMany(answer => answer.Variants)
                .HasForeignKey(variant => variant.GeneratedAnswerId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_tarot_generated_answer_variant_answer");
        });

        modelBuilder.Entity<ClassifierTrainingExampleEntity>(entity =>
        {
            entity.ToTable("tarot_question_classification_training", table =>
            {
                table.HasCheckConstraint("ck_tarot_training_question_hash", "length(question_hash) = 64");
                table.HasCheckConstraint("ck_tarot_training_confidence", "predicted_confidence >= 0 AND predicted_confidence <= 1");
                table.HasCheckConstraint("ck_tarot_training_review_status", "review_status IN ('PENDING', 'APPROVED', 'REJECTED')");
                table.HasCheckConstraint("ck_tarot_training_revision", "revision >= 0");
                table.HasCheckConstraint(
                    "ck_tarot_training_reviewed_personalization",
                    "reviewed_personalization IS NULL OR reviewed_personalization IN ('LOW', 'MEDIUM', 'HIGH')");
                table.HasCheckConstraint(
                    "ck_tarot_training_reviewer_time",
                    "reviewer_time_seconds IS NULL OR reviewer_time_seconds >= 0");
            });
            entity.HasKey(example => example.Id).HasName("pk_tarot_question_classification_training");
            entity.Property(example => example.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(example => example.Question).HasColumnName("question").HasMaxLength(500).IsRequired();
            entity.Property(example => example.QuestionHash).HasColumnName("question_hash").HasMaxLength(64).IsRequired();
            entity.Property(example => example.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
            entity.Property(example => example.PredictedDomain).HasColumnName("predicted_domain").HasMaxLength(50).IsRequired();
            entity.Property(example => example.PredictedIntent).HasColumnName("predicted_intent").HasMaxLength(100).IsRequired();
            entity.Property(example => example.PredictedConfidence).HasColumnName("predicted_confidence").IsRequired();
            entity.Property(example => example.PredictedPersonalization).HasColumnName("predicted_personalization").HasMaxLength(20).IsRequired();
            entity.Property(example => example.ClassifierSource).HasColumnName("classifier_source").HasMaxLength(50).IsRequired();
            entity.Property(example => example.ClassifierModelVersion).HasColumnName("classifier_model_version").HasMaxLength(100);
            entity.Property(example => example.ReviewStatus).HasColumnName("review_status").HasMaxLength(20).IsRequired();
            entity.Property(example => example.ReviewedDomain).HasColumnName("reviewed_domain").HasMaxLength(50);
            entity.Property(example => example.ReviewedIntent).HasColumnName("reviewed_intent").HasMaxLength(100);
            entity.Property(example => example.ReviewedPersonalization).HasColumnName("reviewed_personalization").HasMaxLength(20);
            entity.Property(example => example.ParaphraseGroup).HasColumnName("paraphrase_group").HasMaxLength(100);
            entity.Property(example => example.ReviewerTimeSeconds).HasColumnName("reviewer_time_seconds");
            entity.Property(example => example.ConsentVersion).HasColumnName("consent_version").HasMaxLength(50).IsRequired();
            entity.Property(example => example.Revision).HasColumnName("revision").HasDefaultValue(0).IsRequired();
            entity.Property(example => example.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP").IsRequired();
            entity.Property(example => example.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(example => example.QuestionHash).IsUnique().HasDatabaseName("ux_tarot_training_question_hash");
            entity.HasIndex(example => new { example.ReviewStatus, example.CreatedAt }).HasDatabaseName("ix_tarot_training_review_status_created_at");
        });
    }
}
