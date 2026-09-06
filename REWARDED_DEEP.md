# Rewarded DEEP access

Rewarded DEEP is implemented behind `RewardedDeep:Enabled`, which is `false` in every checked-in environment. Do not enable it merely because the application and tests pass. Production rollout still requires written confirmation of Google Ad Manager rewarded-web eligibility for the publisher, Thailand traffic, production domain, and intended mobile and desktop placements, plus policy, consent, legal, fraud, abuse, and cost approval.

## Runtime configuration

- `REWARDED_DEEP_ENABLED=false` controls both the API and the public web consent surface in Docker Compose.
- `REWARDED_DEEP_AD_UNIT_PATH` is the reviewed Google Ad Manager rewarded ad-unit path. It is public configuration, not a credential.
- `rewarded_deep_settings` is the authoritative singleton for the completion threshold and bundle credit count. Migration `20260905152640_RewardedDeepCredits` seeds `3` completed ads and `1` DEEP credit.
- The API fails closed if the feature flag, ad-unit path, PostgreSQL service, or valid singleton settings are absent.

The configuration values have reviewed bounds of 1–10 completed ads and 1–5 credits. Admins can read and update them at `GET/PUT /api/admin/rewarded-deep/settings`; updates use an expected revision and record the actor and timestamp. Existing sessions retain their snapshotted promise.

## Public flow and API

1. `GET /api/rewards/deep/status` returns server progress, the snapshotted/current requirement and reward, credit availability, expiry, and next eligibility.
2. `POST /api/rewards/deep/sessions` creates or resumes a 24-hour user session. Anonymous visitors receive a secure, HttpOnly, same-site opaque session cookie.
3. `POST /api/rewards/deep/attempts` issues a signed, hashed, short-lived, single-purpose nonce. Only one live attempt is allowed per reward session.
4. The browser loads Google Publisher Tag only after advertising consent and a separate explicit action. It calls the grant endpoint only after GPT emits `rewardedSlotGranted`. Close, skip, error, and no-fill paths close the nonce without progress.
5. `POST /api/rewards/deep/grants` consumes the nonce once and updates progress. At the snapshotted threshold, one transaction resets progress, creates the snapshotted credit count, and applies the rolling 24-hour bundle cap.
6. The DEEP generation endpoint checks premium first. Otherwise it atomically reserves one unexpired reward credit, consumes it only after a valid reading is generated, and releases it when server generation fails.

All mutations require CSRF protection and have bounded rate-limit policies. Swagger and OpenAPI are available only in Development. The reward ledger stores no clicks, reading questions, answers, cards, topics, provider targeting data, or cash value.

## Residual web fraud risk

Google Ad Manager rewarded ads for web do not provide server-side verification. A browser `rewardedSlotGranted` signal can therefore be forged. Signed short-lived nonces, database identity binding, one-time use, replay protection, sequential attempts, rate limits, expiry, atomic updates, and a rolling daily cap reduce abuse but cannot make this event cryptographically authoritative.

The product owner must explicitly accept that residual risk before enabling the feature. AdSense Offerwall cannot be assumed to support the custom three-completion counter: its documented provider-managed rewarded choice is a one-ad content-access model. If the residual web risk is unacceptable, keep this flag off and use that provider-managed one-ad Offerwall model or paid-only DEEP access. Never manually click live ads during testing, and never reward or encourage clicks.

## Verification

Run:

```text
dotnet test TarotDestiny.sln --no-restore
dotnet ef migrations has-pending-model-changes --project apps/api/TarotDestiny.Api.csproj --startup-project apps/api/TarotDestiny.Api.csproj
cd apps/web
npm run build
npm run test:smoke
```

Before rollout, also inspect `/swagger/v1/swagger.json` and `/swagger/index.html` in Development, run the migration against a production-shaped PostgreSQL copy, validate consent in each allowed region, and use provider test inventory rather than interacting with live ads.
