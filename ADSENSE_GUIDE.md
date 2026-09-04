# Google AdSense Setup and Launch Guide

This guide explains how to take the AdSense implementation in this repository from its current safe, disabled state to one live responsive advertisement on each reviewed guide page.

The site is **not ready to serve ads yet**. All sixteen guide files are private drafts, production operator/legal details are unfinished, and AdSense and analytics are disabled by default. Complete every applicable step below in order.

## What This Repository Supports

- One labelled responsive AdSense display unit on each published guide article.
- No display ads on `/th`, `/en`, reading results, legal pages, or `/admin`.
- Non-personalized ad requests only.
- No Google request before the required affirmative consent.
- AdSense verification through the `google-adsense-account` meta tag and `/ads.txt`, even while ad serving is disabled.
- Fail-closed behavior when the domain, publisher ID, slot ID, consent region, or rollout flag is invalid.

This implementation does not enable Auto Ads, anchor ads, vignette ads, or rewarded DEEP access.

## 1. Prepare the Site Before Applying

### Review and publish the guides

The repository contains eight English and eight Thai guide drafts under:

```text
apps/web/content/guides/en/
apps/web/content/guides/th/
```

The owner must personally review every article and confirm its translation, accuracy, authorship, title, description, dates, and ad position. For each approved Thai/English pair, change the frontmatter in both files to:

```yaml
publishedAt: "YYYY-MM-DD"
draft: false
reviewStatus: "reviewed"
```

Do not publish placeholder, unreviewed, automatically generated, or substantially duplicated articles.

Validate all eight pairs:

```powershell
cd apps/web
npm.cmd run validate:content:ready
npm.cmd run typecheck
npm.cmd run build
```

The readiness command must report exactly eight published bilingual pairs.

### Finalize trust information

Before applying, replace every placeholder with real, reviewed information:

- Operator or business identity.
- Monitored contact email address.
- About page.
- Privacy Policy.
- Terms.
- Cookie Policy.
- Disclaimer.
- Thai and English consent wording.

The service must continue to state that it is intended for adults 18+ and is for entertainment and self-reflection, not medical, legal, financial, mental-health, or other professional advice.

## 2. Deploy the Production Domain Safely

Use an HTTPS domain such as `https://example.com`; do not use localhost, an IP address, or a URL with a path as `SITE_URL`.

Before trusting country detection:

1. Proxy the public hostname through Cloudflare.
2. Protect the origin from direct public access using Cloudflare Tunnel, authenticated origin pulls, or firewall rules limited to Cloudflare traffic.
3. Confirm Cloudflare supplies `CF-IPCountry` to the origin and strips any visitor-supplied copy.
4. Do not cache country- or consent-varying HTML across visitors. Caching immutable `/_next/static/` assets is safe.
5. Keep `CF_IPCOUNTRY_TRUSTED=false` until these checks pass.

Unknown countries and untrusted country headers intentionally receive no Google scripts.

## 3. Create the AdSense Account and Add the Site

1. Sign in to [Google AdSense](https://www.google.com/adsense/start/).
2. Create or complete the publisher account using the real payee and payment details.
3. Add the exact production domain under **Sites**.
4. Copy the publisher/client ID. The application requires this exact format:

```text
ca-pub-0000000000000000
```

5. Put the real ID into the production secret environment while keeping ad serving disabled:

```dotenv
ADSENSE_CLIENT_ID=ca-pub-YOUR_16_DIGIT_PUBLISHER_ID
ADSENSE_ENABLED=false
```

6. Redeploy the web container.
7. In AdSense, choose verification using the site meta tag or `ads.txt`.

After deployment, verify:

```text
https://your-domain.example/ads.txt
```

It must contain one exact line using your publisher number:

```text
google.com, pub-YOUR_16_DIGIT_PUBLISHER_ID, DIRECT, f08c47fec0942fa0
```

View the source of `/th` or `/en` and confirm the following meta tag exists with the same account:

```html
<meta name="google-adsense-account" content="ca-pub-YOUR_16_DIGIT_PUBLISHER_ID">
```

The publisher ID is public, but it still must not be confused with a Google account password, OAuth token, or cloud API key.

## 4. Configure Consent in AdSense

In **Privacy & messaging**:

1. Create a European regulations message for the production site.
2. Use Google's certified TCF CMP.
3. Enable the three-choice presentation: **Do not consent**, **Consent**, and **Manage options**.
4. Add both production Privacy Policy URLs.
5. Configure and publish the message before enabling ads.
6. Confirm the message works for EEA, UK, and Swiss traffic.

References:

- [Google CMP requirements](https://support.google.com/adsense/answer/13554116?hl=en)
- [Create a European regulations message](https://support.google.com/adsense/answer/10960768?hl=en-GB)

Thai visitors use the application's separate Analytics and Advertising choices. Appropriate legal review of that wording remains required. Declining Advertising must result in no AdSense request.

## 5. Create the Manual Guide Ad Unit

In AdSense:

1. Open **Ads**.
2. Choose **By ad unit**.
3. Create a **Display ad**.
4. Give it a recognizable name such as `Guide article responsive`.
5. Select **Responsive** size.
6. Save the unit.
7. From the generated code, copy only the numeric `data-ad-slot` value. The application requires exactly ten digits.

Configure it in the production environment:

```dotenv
ADSENSE_GUIDE_SLOT_ID=YOUR_10_DIGIT_SLOT_ID
```

Do not paste Google's full JavaScript snippet into MDX articles. The application already loads the library once and owns the single safe placement.

Keep these AdSense features disabled:

- Auto Ads.
- Anchor ads.
- Vignette ads.
- Additional manual units.

## 6. Configure the Production Environment

Use a secret environment file outside the repository or the deployment platform's secret manager. Do not commit production values.

Minimum web configuration before AdSense review:

```dotenv
ASPNETCORE_ENVIRONMENT=Production
SITE_URL=https://your-domain.example
CONTACT_EMAIL=contact@your-domain.example
ADSENSE_CLIENT_ID=ca-pub-YOUR_16_DIGIT_PUBLISHER_ID
ADSENSE_GUIDE_SLOT_ID=YOUR_10_DIGIT_SLOT_ID
ADSENSE_ENABLED=false
GA_MEASUREMENT_ID=
ANALYTICS_ENABLED=false
CF_IPCOUNTRY_TRUSTED=false
```

After Cloudflare origin protection and country-header tests pass:

```dotenv
CF_IPCOUNTRY_TRUSTED=true
```

Example Docker Compose deployment:

```powershell
docker compose --env-file C:\secure\destiny.production.env up -d --build
```

Do not use the development credentials or defaults from `.env.example` in Production.

## 7. Perform the Pre-Review Checks

Confirm all of the following before requesting AdSense review:

- `/` permanently redirects to `/th`.
- `/th` and `/en` contain useful publisher content and no display ad.
- Both guide indexes list eight reviewed articles.
- Every guide is reachable in both languages.
- About, Contact, Privacy, Terms, Cookie Policy, and Disclaimer pages are reachable from the footer.
- `/sitemap.xml` contains all published guide pairs and excludes drafts and `/admin`.
- `/robots.txt` advertises the sitemap and disallows `/admin`.
- `/ads.txt` contains the exact seller line.
- `/admin` is noindex and contains no Google code.
- Consent can be changed or withdrawn from the footer.
- No Google request occurs before applicable consent.
- No horizontal overflow occurs at 360 px or 1440 px.
- No link, button, card, or reading control is close enough to the future ad space to cause accidental clicks.

Run the automated checks:

```powershell
cd apps/web
npm.cmd run validate:content:ready
npm.cmd run typecheck
npm.cmd run build
npm.cmd run test:smoke
```

## 8. Request AdSense Review

1. Confirm the live domain is indexable and all navigation works without signing in.
2. Confirm the verification meta tag and `ads.txt` use the same publisher ID.
3. Submit the site for review in AdSense.
4. Keep `ADSENSE_ENABLED=false` while review is pending.
5. Correct any content, navigation, policy, or ownership problems reported by AdSense.
6. Wait until the site status is **Ready**.

Do not create artificial traffic, buy low-quality traffic, repeatedly reload ad pages, or click a live ad. Google prohibits publishers from clicking their own ads or encouraging visitors to click them. See [AdSense program policies](https://support.google.com/adsense/answer/48182?hl=en).

## 9. Enable the Guide Advertisement

Only after the site is Ready and consent tests pass:

```dotenv
ADSENSE_ENABLED=true
```

Redeploy or restart the web service so it receives the new environment value.

Expected behavior:

| Page or condition | Expected result |
| --- | --- |
| Published guide with affirmative advertising consent | One labelled responsive non-personalized ad request |
| Published guide without advertising consent | No AdSense request and no empty ad container |
| `/th` or `/en` reading experience | No display ad |
| Reading result | No display ad |
| Legal or trust page | No display ad |
| `/admin` | No Google script or ad |
| Draft or invalid guide | Not publicly routable |
| Invalid/missing ID, non-HTTPS site, Development, or unknown region | No Google request |

An empty or unfilled ad is not necessarily an application error. AdSense may have no suitable demand, the account may still be limited, or the visitor may not have granted consent.

## 10. Optional GA4 Rollout

GA4 is independent of AdSense. Leave it disabled unless measurement has passed the same privacy and consent review.

```dotenv
GA_MEASUREMENT_ID=G-YOUR_MEASUREMENT_ID
ANALYTICS_ENABLED=true
```

The implementation records only a sanitized published-guide route and locale. It must never receive questions, generated readings, card selections, topics, user identifiers, authentication data, or admin activity.

## 11. Rewarded Ads and DEEP Access

The responsive guide ad above is a normal display ad. Viewing it or clicking it must never unlock DEEP access.

The requested `three completed ads → one DEEP reading` feature requires a separate rewarded-ad integration, an explicit opt-in before every ad, provider completion events, and a server-side single-use credit ledger. Google permits a fixed bundle of rewarded ads only when the required action and promised reward are clearly disclosed. See [Google rewarded-ad policies](https://support.google.com/adsense/answer/9121589?hl=en).

AdSense Offerwall can provide Google-managed access after a rewarded ad, but it should not be assumed to implement this application's custom three-completion counter. See [AdSense Offerwall rewarded choice](https://support.google.com/adsense/answer/12726063?hl=en).

The custom rewarded-DEEP feature remains an unfinished task in `TASK.md`. Do not count normal impressions, clicks, partial views, closes, errors, or no-fill responses as reward progress.

## 12. Monthly Operations

After launch, review at least monthly:

- AdSense Policy Center and site status.
- Invalid traffic warnings.
- Country and consent behavior.
- Engagement and revenue by guide, without collecting reading content.
- Core Web Vitals and layout shift.
- Broken legal, navigation, sitemap, and `ads.txt` URLs.
- Content accuracy and update dates.

If Google reports a policy problem or traffic anomaly, disable serving immediately:

```dotenv
ADSENSE_ENABLED=false
```

Then redeploy while investigating. The site and reading experience will continue working without ads.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No verification meta tag | `ADSENSE_CLIENT_ID` must be `ca-pub-` followed by exactly sixteen digits. |
| Empty `/ads.txt` | Confirm the same valid publisher ID reached the running web container. |
| Guide returns 404 | Its Thai/English pair is still a draft or has invalid review metadata. |
| No ad container | This is expected when configuration, publication, consent, region, or rollout checks fail. |
| AdSense script does not load | Confirm Production, HTTPS `SITE_URL`, Ready status, trusted region handling, affirmative Advertising consent, valid IDs, and `ADSENSE_ENABLED=true`. |
| EEA message does not appear | Confirm the Google message is published for the exact domain and the country header is trusted. |
| Ads appear on reading or admin pages | Disable `ADSENSE_ENABLED` immediately and treat it as a release-blocking defect. |
| Layout moves when the ad loads | Verify the reserved guide-ad space has not been removed or overridden by CSS. |

This guide is an operational checklist, not legal or tax advice. AdSense acceptance and continued serving remain Google's decisions.
