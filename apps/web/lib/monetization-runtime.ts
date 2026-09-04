import "server-only";

import {
  parseAdsenseClientId,
  parseAdsensePublisherId,
  parseAdsenseSlotId,
  parseExactTrue,
  parseGa4MeasurementId,
  parseSiteOrigin,
} from "./monetization-validation";

export {
  parseAdsenseClientId,
  parseAdsenseSlotId,
  parseGa4MeasurementId,
} from "./monetization-validation";

export interface AdsenseRuntimeConfig {
  readonly enabled: boolean;
  readonly clientId: string | null;
  readonly publisherId: string | null;
  readonly guideSlotId: string | null;
  readonly verificationMeta: string | null;
  readonly adsTxtLine: string | null;
}

export interface AnalyticsRuntimeConfig {
  readonly enabled: boolean;
  readonly measurementId: string | null;
}

export interface MonetizationRuntimeConfig {
  readonly production: boolean;
  readonly siteUrl: string | null;
  readonly trustCloudflareCountryHeader: boolean;
  readonly adsense: AdsenseRuntimeConfig;
  readonly analytics: AnalyticsRuntimeConfig;
}

type RuntimeEnvironment = Readonly<
  Partial<
    Record<
      | "NODE_ENV"
      | "SITE_URL"
      | "CF_IPCOUNTRY_TRUSTED"
      | "ADSENSE_CLIENT_ID"
      | "ADSENSE_GUIDE_SLOT_ID"
      | "ADSENSE_ENABLED"
      | "GA_MEASUREMENT_ID"
      | "ANALYTICS_ENABLED",
      string
    >
  >
>;

/**
 * Reads and validates server-only runtime settings. Invalid, incomplete, or
 * non-production settings always disable outbound Google tags.
 */
export function getMonetizationRuntimeConfig(
  environment: RuntimeEnvironment = process.env,
): MonetizationRuntimeConfig {
  const production = environment.NODE_ENV === "production";
  const siteUrl = parseSiteOrigin(environment.SITE_URL, production);
  const clientId = parseAdsenseClientId(environment.ADSENSE_CLIENT_ID);
  const guideSlotId = parseAdsenseSlotId(
    environment.ADSENSE_GUIDE_SLOT_ID,
  );
  const measurementId = parseGa4MeasurementId(
    environment.GA_MEASUREMENT_ID,
  );
  const publisherId = parseAdsensePublisherId(environment.ADSENSE_CLIENT_ID);
  const canSendGoogleRequests = production && siteUrl !== null;

  return {
    production,
    siteUrl,
    trustCloudflareCountryHeader:
      production && parseExactTrue(environment.CF_IPCOUNTRY_TRUSTED),
    adsense: {
      enabled:
        canSendGoogleRequests &&
        parseExactTrue(environment.ADSENSE_ENABLED) &&
        clientId !== null &&
        guideSlotId !== null,
      clientId,
      publisherId,
      guideSlotId,
      // Verification is deliberately independent of ad serving. A site may be
      // verified while the ADSENSE_ENABLED rollout switch remains off.
      verificationMeta: clientId,
      adsTxtLine: publisherId
        ? `google.com, ${publisherId}, DIRECT, f08c47fec0942fa0`
        : null,
    },
    analytics: {
      enabled:
        canSendGoogleRequests &&
        parseExactTrue(environment.ANALYTICS_ENABLED) &&
        measurementId !== null,
      measurementId,
    },
  };
}
