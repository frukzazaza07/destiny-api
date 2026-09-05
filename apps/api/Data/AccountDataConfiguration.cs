using Microsoft.EntityFrameworkCore;

namespace TarotDestiny.Api.Data;

internal static class AccountDataConfiguration
{
    internal static readonly Guid UserRoleId = Guid.Parse("0d80c432-36ca-4b4c-90a5-3a824e5c73c1");
    internal static readonly Guid AdminRoleId = Guid.Parse("854ced1e-2943-42fc-9818-01a47952c723");

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("account_user", table =>
            {
                table.HasCheckConstraint("ck_account_user_failed_count", "access_failed_count >= 0");
                table.HasCheckConstraint("ck_account_user_revision", "revision >= 0");
            });
            entity.HasKey(user => user.Id).HasName("pk_account_user");
            entity.Property(user => user.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(user => user.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasColumnName("password_hash").HasMaxLength(1000).IsRequired();
            entity.Property(user => user.EmailVerifiedAt).HasColumnName("email_verified_at").HasColumnType("timestamp with time zone");
            entity.Property(user => user.DisabledAt).HasColumnName("disabled_at").HasColumnType("timestamp with time zone");
            entity.Property(user => user.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).IsRequired();
            entity.Property(user => user.AccessFailedCount).HasColumnName("access_failed_count").HasDefaultValue(0).IsRequired();
            entity.Property(user => user.LockoutEnd).HasColumnName("lockout_end").HasColumnType("timestamp with time zone");
            entity.Property(user => user.Revision).HasColumnName("revision").HasDefaultValue(0).IsConcurrencyToken();
            entity.Property(user => user.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(user => user.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("ux_account_user_normalized_email");
        });

        modelBuilder.Entity<AccountRoleEntity>(entity =>
        {
            entity.ToTable("account_role");
            entity.HasKey(role => role.Id).HasName("pk_account_role");
            entity.Property(role => role.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(role => role.Name).HasColumnName("name").HasMaxLength(30).IsRequired();
            entity.HasIndex(role => role.Name).IsUnique().HasDatabaseName("ux_account_role_name");
            entity.HasData(
                new AccountRoleEntity { Id = UserRoleId, Name = AccountRoles.User },
                new AccountRoleEntity { Id = AdminRoleId, Name = AccountRoles.Admin });
        });

        modelBuilder.Entity<UserAccountRoleEntity>(entity =>
        {
            entity.ToTable("account_user_role");
            entity.HasKey(item => new { item.UserId, item.RoleId }).HasName("pk_account_user_role");
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.RoleId).HasColumnName("role_id");
            entity.HasOne(item => item.User).WithMany(user => user.Roles).HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Role).WithMany(role => role.Users).HasForeignKey(item => item.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserSessionEntity>(entity =>
        {
            entity.ToTable("account_session", table =>
                table.HasCheckConstraint("ck_account_session_expiry", "expires_at > created_at"));
            entity.HasKey(session => session.Id).HasName("pk_account_session");
            entity.Property(session => session.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(session => session.UserId).HasColumnName("user_id");
            entity.Property(session => session.SecurityStamp).HasColumnName("security_stamp").HasMaxLength(64).IsRequired();
            entity.Property(session => session.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(session => session.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(session => session.LastSeenAt).HasColumnName("last_seen_at").HasColumnType("timestamp with time zone");
            entity.Property(session => session.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            entity.HasIndex(session => new { session.UserId, session.ExpiresAt }).HasDatabaseName("ix_account_session_user_expiry");
            entity.HasOne(session => session.User).WithMany(user => user.Sessions).HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAccountTokenEntity>(entity =>
        {
            entity.ToTable("account_token", table =>
                table.HasCheckConstraint("ck_account_token_expiry", "expires_at > created_at"));
            entity.HasKey(token => token.Id).HasName("pk_account_token");
            entity.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(token => token.UserId).HasColumnName("user_id");
            entity.Property(token => token.Purpose).HasColumnName("purpose").HasMaxLength(40).IsRequired();
            entity.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(token => token.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(token => token.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(token => token.ConsumedAt).HasColumnName("consumed_at").HasColumnType("timestamp with time zone").IsConcurrencyToken();
            entity.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_account_token_hash");
            entity.HasIndex(token => new { token.UserId, token.Purpose }).HasDatabaseName("ix_account_token_user_purpose");
            entity.HasOne(token => token.User).WithMany().HasForeignKey(token => token.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserEntitlementEntity>(entity =>
        {
            entity.ToTable("user_entitlement", table =>
            {
                table.HasCheckConstraint("ck_user_entitlement_expiry", "expires_at > starts_at");
                table.HasCheckConstraint("ck_user_entitlement_revision", "revision >= 0");
            });
            entity.HasKey(item => item.Id).HasName("pk_user_entitlement");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
            entity.Property(item => item.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp with time zone");
            entity.Property(item => item.GrantedByUserId).HasColumnName("granted_by_user_id");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0).IsConcurrencyToken();
            entity.HasIndex(item => new { item.UserId, item.Type }).IsUnique().HasDatabaseName("ux_user_entitlement_user_type");
            entity.HasOne(item => item.User).WithMany(user => user.Entitlements).HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.GrantedByUser).WithMany().HasForeignKey(item => item.GrantedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserEntitlementAuditEntity>(entity =>
        {
            entity.ToTable("user_entitlement_audit");
            entity.HasKey(item => item.Id).HasName("pk_user_entitlement_audit");
            entity.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.EntitlementId).HasColumnName("entitlement_id");
            entity.Property(item => item.Action).HasColumnName("action").HasMaxLength(30).IsRequired();
            entity.Property(item => item.StartsAt).HasColumnName("starts_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.Property(item => item.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(item => item.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(item => new { item.UserId, item.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_user_entitlement_audit_user_created");
        });
    }
}
