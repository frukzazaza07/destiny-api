import type { PrivacyRegion } from "./privacy-region";

export const CONSENT_POLICY_VERSION = 1 as const;
export const CONSENT_STORAGE_KEY =
  "tarot-destiny:privacy-consent:v1" as const;
export const CONSENT_SETTINGS_EVENT =
  "tarot-destiny:open-consent-settings" as const;

export type ConsentSource =
  | "initial"
  | "stored"
  | "choice"
  | "google-cmp"
  | "restricted";

export interface ConsentSnapshot {
  readonly region: PrivacyRegion;
  readonly ready: boolean;
  readonly analytics: boolean;
  readonly advertising: boolean;
  readonly source: ConsentSource;
}

export interface OptionalGoogleServices {
  readonly analytics: boolean;
  readonly advertising: boolean;
}

export interface StoredThailandConsent {
  readonly version: typeof CONSENT_POLICY_VERSION;
  readonly region: "thailand";
  readonly analytics: boolean;
  readonly advertising: boolean;
  readonly updatedAt: string;
}

export function initialConsentSnapshot(
  region: PrivacyRegion,
): ConsentSnapshot {
  if (region === "restricted") {
    return {
      region,
      ready: true,
      analytics: false,
      advertising: false,
      source: "restricted",
    };
  }

  return {
    region,
    ready: false,
    analytics: false,
    advertising: false,
    source: "initial",
  };
}

export function parseStoredThailandConsent(
  serialized: string | null,
): StoredThailandConsent | null {
  if (!serialized) return null;

  try {
    const value: unknown = JSON.parse(serialized);
    if (!isRecord(value)) return null;

    if (
      value.version !== CONSENT_POLICY_VERSION ||
      value.region !== "thailand" ||
      typeof value.analytics !== "boolean" ||
      typeof value.advertising !== "boolean" ||
      typeof value.updatedAt !== "string" ||
      !isIsoTimestamp(value.updatedAt)
    ) {
      return null;
    }

    return {
      version: CONSENT_POLICY_VERSION,
      region: "thailand",
      analytics: value.analytics,
      advertising: value.advertising,
      updatedAt: value.updatedAt,
    };
  } catch {
    return null;
  }
}

export function createStoredThailandConsent(
  analytics: boolean,
  advertising: boolean,
  now = new Date(),
): StoredThailandConsent {
  return {
    version: CONSENT_POLICY_VERSION,
    region: "thailand",
    analytics,
    advertising,
    updatedAt: now.toISOString(),
  };
}

/**
 * Lets any Client Component open the settings UI. Server-rendered controls can
 * instead use the `data-consent-settings` attribute; ConsentProvider delegates
 * clicks for that attribute.
 */
export function openConsentSettings(): void {
  if (typeof window === "undefined") return;
  window.dispatchEvent(new Event(CONSENT_SETTINGS_EVENT));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isIsoTimestamp(value: string): boolean {
  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) && new Date(timestamp).toISOString() === value;
}
