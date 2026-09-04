export const PRIVACY_REGIONS = ["thailand", "eea", "restricted"] as const;

export type PrivacyRegion = (typeof PRIVACY_REGIONS)[number];

export interface PrivacyRegionResolution {
  readonly region: PrivacyRegion;
  readonly countryCode: string | null;
  readonly source: "cloudflare" | "untrusted" | "missing" | "invalid";
}

export interface HeaderReader {
  get(name: string): string | null;
}

const EEA_UK_SWITZERLAND = new Set([
  "AT",
  "BE",
  "BG",
  "CH",
  "CY",
  "CZ",
  "DE",
  "DK",
  "EE",
  "ES",
  "FI",
  "FR",
  "GB",
  "GR",
  "HR",
  "HU",
  "IE",
  "IS",
  "IT",
  "LI",
  "LT",
  "LU",
  "LV",
  "MT",
  "NL",
  "NO",
  "PL",
  "PT",
  "RO",
  "SE",
  "SI",
  "SK",
]);

/**
 * Resolves the privacy treatment from Cloudflare's country header. The caller
 * must explicitly attest that the origin only accepts trusted Cloudflare
 * traffic; otherwise even a plausible header is ignored.
 */
export function resolvePrivacyRegion(
  headers: HeaderReader,
  trustCloudflareHeader = false,
): PrivacyRegionResolution {
  if (!trustCloudflareHeader) {
    return {
      region: "restricted",
      countryCode: null,
      source: "untrusted",
    };
  }

  return classifyTrustedCloudflareCountry(headers.get("CF-IPCountry"));
}

export function classifyTrustedCloudflareCountry(
  value: string | null | undefined,
): PrivacyRegionResolution {
  if (value === null || value === undefined || value.trim() === "") {
    return {
      region: "restricted",
      countryCode: null,
      source: "missing",
    };
  }

  const countryCode = value.trim().toUpperCase();
  if (!/^[A-Z]{2}$/.test(countryCode)) {
    return {
      region: "restricted",
      countryCode: null,
      source: "invalid",
    };
  }

  if (countryCode === "TH") {
    return { region: "thailand", countryCode, source: "cloudflare" };
  }

  if (EEA_UK_SWITZERLAND.has(countryCode)) {
    return { region: "eea", countryCode, source: "cloudflare" };
  }

  // No jurisdiction is assumed safe by default. Additional country policies
  // must be reviewed and deliberately added before tags can run there.
  return { region: "restricted", countryCode, source: "cloudflare" };
}
