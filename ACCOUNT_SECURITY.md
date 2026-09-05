# Account and Premium Access Operations

The account implementation uses an `HttpOnly`, `Secure`, `SameSite=Lax` authentication cookie backed by a revocable database session. Passwords use ASP.NET Core's adaptive PBKDF2 password hasher. The browser never receives an admin key or a premium entitlement claim it can authorize on its own.

## Deploy and migrate

Run the existing migration service before the API. Migration `UserAccountsAndPremiumAccess` creates users, seeded `USER` and `ADMIN` roles, sessions, one-time account tokens, premium entitlements, and entitlement audit records. It creates no user, password, or entitlement.

The API container persists ASP.NET Core Data Protection keys in the `account-dataprotection` volume. Back up and restrict that volume like authentication secrets; replacing it signs every browser out. Use one shared protected key ring for all API replicas.

## Create the first administrator once

1. Set `BOOTSTRAP_ADMIN_EMAIL` and a unique strong `BOOTSTRAP_ADMIN_PASSWORD` in the deployment environment.
2. Run `docker compose run --rm api --bootstrap-admin` after migrations complete.
3. Remove both bootstrap values from the runtime environment.

The command fails closed when an administrator already exists. There is no default credential. Later users and administrators are created from the role-protected `/admin` console.

## Registration and email

Public registration is off by default. Keep `ACCOUNT_PUBLIC_REGISTRATION=false` until SMTP delivery, abuse monitoring, legal copy, and support procedures are ready. Configure the `ACCOUNT_EMAIL_*` settings only through deployment secrets. Verification and reset tokens are random, stored only as SHA-256 hashes, single-use, and short-lived. Email links place the raw token in a URL fragment, which is not sent in HTTP requests or access logs, and token values are never written to application logs.

## Revocation and retention

Password reset, account disablement, and premium grant/extension/revocation revoke the affected user's active sessions. On the next login, every request validates the database session and current entitlement, so disabled, expired, or revoked access cannot survive in an old browser claim.

Account deletion currently performs a security-preserving soft disable and session revocation. Before production launch, the operator must publish reviewed retention periods for account identity, security logs, one-time token records, sessions, and entitlement/audit records, plus a contact process for access, correction, and deletion requests. Do not attach reading questions or generated answers to account advertising records.
