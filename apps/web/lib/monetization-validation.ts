const ADSENSE_CLIENT_ID_PATTERN = /^ca-pub-\d{16}$/;
const ADSENSE_SLOT_ID_PATTERN = /^\d{10}$/;
const GA4_MEASUREMENT_ID_PATTERN = /^G-[A-Z0-9]{6,20}$/;

export function parseAdsenseClientId(
  value: string | undefined,
): string | null {
  const normalized = value?.trim() ?? "";
  return ADSENSE_CLIENT_ID_PATTERN.test(normalized) ? normalized : null;
}

export function parseAdsensePublisherId(
  value: string | undefined,
): string | null {
  const clientId = parseAdsenseClientId(value);
  return clientId?.slice("ca-".length) ?? null;
}

export function parseAdsenseSlotId(
  value: string | undefined,
): string | null {
  const normalized = value?.trim() ?? "";
  return ADSENSE_SLOT_ID_PATTERN.test(normalized) ? normalized : null;
}

export function parseGa4MeasurementId(
  value: string | undefined,
): string | null {
  const normalized = value?.trim().toUpperCase() ?? "";
  return GA4_MEASUREMENT_ID_PATTERN.test(normalized) ? normalized : null;
}

export function parseExactTrue(value: string | undefined): boolean {
  return value?.trim() === "true";
}

export function isMonetizableGuidePath(
  pathname: string,
  locale?: "en" | "th",
): boolean {
  const localePattern = locale ?? "(?:en|th)";
  const pattern = new RegExp(
    `^/${localePattern}/guides/[a-z0-9]+(?:-[a-z0-9]+)*/?$`,
  );
  return pattern.test(pathname);
}

export function parseSiteOrigin(
  value: string | undefined,
  requireHttps: boolean,
): string | null {
  const normalized = value?.trim();
  if (!normalized) return null;

  try {
    const parsed = new URL(normalized);
    const isWebUrl = parsed.protocol === "https:" || parsed.protocol === "http:";
    const hasOnlyOrigin =
      parsed.username === "" &&
      parsed.password === "" &&
      parsed.pathname === "/" &&
      parsed.search === "" &&
      parsed.hash === "";

    if (!isWebUrl || !hasOnlyOrigin) return null;
    if (requireHttps && parsed.protocol !== "https:") return null;

    return parsed.origin;
  } catch {
    return null;
  }
}
