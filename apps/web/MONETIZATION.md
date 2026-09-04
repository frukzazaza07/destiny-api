# Monetization Operations

The implementation is intentionally in pre-launch mode. AdSense and GA4 are disabled unless every required server-side value is valid, the corresponding feature flag is exactly `true`, the process is running in Production, and `SITE_URL` is an HTTPS origin. Unreviewed guides are not routable and never enter the sitemap.

## Runtime configuration

Configure these values in the deployment environment; do not commit production values:

| Variable | Purpose | Safe default |
| --- | --- | --- |
| `SITE_URL` | Canonical HTTPS origin, with no path, query, credentials, or fragment | unset/invalid disables Google requests and indexing |
| `CONTACT_EMAIL` | Monitored public operator contact address | unset shows a launch blocker |
| `ADSENSE_CLIENT_ID` | AdSense client ID in `ca-pub-0000000000000000` form | unset |
| `ADSENSE_GUIDE_SLOT_ID` | Ten-digit responsive guide ad-unit slot | unset |
| `ADSENSE_ENABLED` | Manual guide ad serving switch | `false` |
| `GA_MEASUREMENT_ID` | GA4 ID in `G-...` form | unset |
| `ANALYTICS_ENABLED` | Sanitized guide-page measurement switch | `false` |
| `CF_IPCOUNTRY_TRUSTED` | Allows server use of Cloudflare's `CF-IPCountry` header | `false` |

A valid publisher ID exposes the `google-adsense-account` verification meta tag and exact Google seller line at `/ads.txt` even while `ADSENSE_ENABLED=false`. Invalid or missing values produce no Google script and no empty ad container.

## Content release gate

The 16 MDX files under `content/guides/{th,en}` are substantial editorial drafts. Their frontmatter is the typed catalog source used by routes, metadata, and the sitemap. Before changing any guide from draft status, the owner must personally review both translations and confirm the author, title, description, article text, publication date, update date, and single ad position.

For each approved pair, set both files to:

```yaml
publishedAt: "YYYY-MM-DD"
draft: false
reviewStatus: "reviewed"
```

Run:

```text
npm run validate:content
npm run validate:content:ready
npm run typecheck
npm run build
```

`validate:content:ready` must report exactly eight publication-ready bilingual pairs. It intentionally fails while the current owner-review blockers remain.

## Cloudflare prerequisite

Before setting `CF_IPCOUNTRY_TRUSTED=true`:

1. Proxy the production hostname through Cloudflare with HTTPS and strict origin validation.
2. Block direct public access to the origin using a tunnel, authenticated origin pull, or firewall rules limited to Cloudflare's published networks.
3. Confirm Cloudflare supplies `CF-IPCountry` to the origin and strips any visitor-supplied copy of that header.
4. Do not override the application's private/no-store response policy for country-dependent HTML. Cache immutable `/_next/static/` assets, but do not share consent-varying HTML across countries or consent states.
5. Test `TH`, an EEA country, and missing/invalid country values at the protected origin before enabling either Google feature.

With the trust flag off, every country is treated as restricted and no Google request is allowed.

## AdSense and consent rollout

1. Add the production site in AdSense using the verification meta tag or `/ads.txt`; keep ad serving off.
2. In AdSense Privacy & messaging, create and publish a European regulations message for the site. Enable the three-choice experience (`Do not consent`, `Consent`, and `Manage options`), use the certified Google TCF v2.3 CMP, add the production privacy-policy URLs, and enable the required consent-mode purposes. See [Google's European message setup](https://support.google.com/adsense/answer/10960768?hl=en-GB) and [CMP requirement](https://support.google.com/adsense/answer/13554116?hl=en).
3. Have appropriate counsel review the Thai and English consent text, operator details, Privacy Policy, Terms, Cookie Policy, and Disclaimer. Code structure reduces data exposure but is not legal advice or a compliance guarantee.
4. Create one manual responsive display unit and configure its ten-digit slot ID. Keep Auto Ads, anchors, and vignettes off.
5. Wait until AdSense marks the site Ready. Verify that the publisher and slot IDs match the approved account.
6. Test consent withdrawal, no-fill, blocked scripts, 360px and 1440px layouts, and accidental-click spacing without clicking any live ad.
7. Enable only the required switch. `ADSENSE_ENABLED=true` permits one non-personalized unit after affirmative advertising consent on a reviewed guide; `ANALYTICS_ENABLED=true` permits only guide pathname and locale after affirmative analytics consent.

The reading experience, reading results, legal pages, and `/admin` have no ad unit. `/admin` uses a separate root layout and is `noindex`. GA4 never receives reading questions, answers, selected cards/topics, user IDs, authentication data, or admin activity.

## Verification

The production browser suite uses fake-shaped public IDs with both feature flags off and verifies redirect behavior, locale metadata, trust routes, draft exclusion, `robots.txt`, `sitemap.xml`, `ads.txt`, country handling, mobile/desktop overflow, keyboard access, and the absence of Google requests on reading and admin routes:

```text
npm run build
npm run test:smoke
```

Live-ad testing must use provider-approved test mechanisms. Never click a live ad, ask a visitor to click, or exchange any benefit for a standard ad click.

## Rewarded DEEP access

The requested “three ads for one DEEP reading” feature is specified separately in the repository `TASK.md`. It must count only three explicit, completed rewarded-ad grant events—never clicks on AdSense display ads. Production implementation remains blocked on rewarded-web provider eligibility and acceptance of the web verification/abuse model.
