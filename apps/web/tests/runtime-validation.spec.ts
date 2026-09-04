import { expect, test } from "@playwright/test";

import {
  isMonetizableGuidePath,
  parseAdsenseClientId,
  parseAdsensePublisherId,
  parseAdsenseSlotId,
  parseExactTrue,
  parseGa4MeasurementId,
  parseSiteOrigin
} from "../lib/monetization-validation";
import {
  CONSENT_POLICY_VERSION,
  createStoredThailandConsent,
  initialConsentSnapshot,
  parseStoredThailandConsent
} from "../lib/consent-preferences";
import { resolvePrivacyRegion } from "../lib/privacy-region";

test.describe("fail-closed runtime validation", () => {
  test("accepts only exact public Google identifier formats", () => {
    expect(parseAdsenseClientId("ca-pub-0000000000000001")).toBe(
      "ca-pub-0000000000000001"
    );
    expect(parseAdsensePublisherId("ca-pub-0000000000000001")).toBe(
      "pub-0000000000000001"
    );
    expect(parseAdsenseClientId("pub-0000000000000001")).toBeNull();
    expect(parseAdsenseSlotId("0000000001")).toBe("0000000001");
    expect(parseAdsenseSlotId("123")).toBeNull();
    expect(parseGa4MeasurementId("g-test000001")).toBe("G-TEST000001");
    expect(parseGa4MeasurementId("UA-123-1")).toBeNull();
  });

  test("requires exact true and an origin-only HTTPS production URL", () => {
    expect(parseExactTrue("true")).toBe(true);
    expect(parseExactTrue("TRUE")).toBe(false);
    expect(parseExactTrue("1")).toBe(false);
    expect(parseSiteOrigin("https://example.com", true)).toBe(
      "https://example.com"
    );
    expect(parseSiteOrigin("http://example.com", true)).toBeNull();
    expect(parseSiteOrigin("https://example.com/path", true)).toBeNull();
    expect(parseSiteOrigin("http://localhost:3000", false)).toBe(
      "http://localhost:3000"
    );
  });

  test("allow-lists only localized guide article paths", () => {
    expect(isMonetizableGuidePath("/th/guides/daily-one-card-reading")).toBe(
      true
    );
    expect(isMonetizableGuidePath("/en/guides/daily-one-card-reading", "en")).toBe(
      true
    );
    expect(isMonetizableGuidePath("/th")).toBe(false);
    expect(isMonetizableGuidePath("/admin")).toBe(false);
    expect(isMonetizableGuidePath("/en/privacy")).toBe(false);
    expect(isMonetizableGuidePath("/en/guides/a?question=secret")).toBe(false);
  });
});

test.describe("region and consent rules", () => {
  const header = (value: string | null) => ({ get: () => value });

  test("ignores even plausible country headers until Cloudflare is trusted", () => {
    expect(resolvePrivacyRegion(header("TH"), false)).toMatchObject({
      region: "restricted",
      source: "untrusted"
    });
  });

  test("classifies Thailand and EEA while restricting unknown countries", () => {
    expect(resolvePrivacyRegion(header("TH"), true).region).toBe("thailand");
    expect(resolvePrivacyRegion(header("DE"), true).region).toBe("eea");
    expect(resolvePrivacyRegion(header("GB"), true).region).toBe("eea");
    expect(resolvePrivacyRegion(header("CH"), true).region).toBe("eea");
    expect(resolvePrivacyRegion(header("US"), true).region).toBe("restricted");
    expect(resolvePrivacyRegion(header(null), true).region).toBe("restricted");
  });

  test("stored Thai choices are versioned and malformed data never grants consent", () => {
    const stored = createStoredThailandConsent(true, false, new Date("2026-09-03T00:00:00.000Z"));
    expect(parseStoredThailandConsent(JSON.stringify(stored))).toEqual(stored);
    expect(parseStoredThailandConsent("not-json")).toBeNull();
    expect(
      parseStoredThailandConsent(
        JSON.stringify({ ...stored, version: CONSENT_POLICY_VERSION + 1 })
      )
    ).toBeNull();
    expect(initialConsentSnapshot("restricted")).toMatchObject({
      ready: true,
      analytics: false,
      advertising: false
    });
    expect(initialConsentSnapshot("thailand")).toMatchObject({
      ready: false,
      analytics: false,
      advertising: false
    });
  });
});
