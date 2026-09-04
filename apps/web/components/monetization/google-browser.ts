"use client";

import "client-only";

import {
  parseAdsenseClientId,
  parseAdsensePublisherId,
  parseGa4MeasurementId,
} from "../../lib/monetization-validation";

export type GoogleConsentValue = "granted" | "denied";

export interface GoogleConsentSelection {
  readonly analytics: boolean;
  readonly advertising: boolean;
}

interface GoogleConsentModeStatus {
  readonly adStoragePurposeConsentStatus: number;
  readonly adUserDataPurposeConsentStatus: number;
  readonly adPersonalizationPurposeConsentStatus: number;
  readonly analyticsStoragePurposeConsentStatus: number;
}

interface GoogleConsentModePurposeStatusEnum {
  readonly GRANTED?: number;
  readonly CONSENT_MODE_PURPOSE_STATUS_GRANTED?: number;
}

interface GoogleFundingChoices {
  callbackQueue?: Array<Record<string, () => void>>;
  ConsentModePurposeStatusEnum?: GoogleConsentModePurposeStatusEnum;
  getGoogleConsentModeValues?: () => GoogleConsentModeStatus;
  showRevocationMessage?: () => void;
}

type GoogleTagArguments = readonly unknown[];
type GoogleTagFunction = (...args: GoogleTagArguments) => void;
type AdsByGoogleQueue = Array<Record<string, unknown>> & {
  pauseAdRequests?: number;
  requestNonPersonalizedAds?: number;
};

interface GoogleClientState {
  readonly scriptLoads: Map<string, Promise<void>>;
  readonly analyticsConfigured: Set<string>;
  readonly lastAnalyticsPage: Map<string, string>;
  readonly initializedAdElements: WeakSet<HTMLElement>;
  readonly activeAdPageClaims: Map<string, symbol>;
  consentDefaultsQueued: boolean;
}

declare global {
  interface Window {
    dataLayer?: GoogleTagArguments[];
    gtag?: GoogleTagFunction;
    adsbygoogle?: AdsByGoogleQueue;
    googlefc?: GoogleFundingChoices;
    __tarotGoogleClientState?: GoogleClientState;
  }
}

export function initializeGoogleConsentDefaults(): void {
  const state = getGoogleClientState();
  if (state.consentDefaultsQueued) return;

  getGoogleTag()("consent", "default", {
    ad_storage: "denied",
    ad_user_data: "denied",
    ad_personalization: "denied",
    analytics_storage: "denied",
  });
  state.consentDefaultsQueued = true;
}

export function updateGoogleConsent(
  selection: GoogleConsentSelection,
): void {
  initializeGoogleConsentDefaults();

  getGoogleTag()("consent", "update", {
    ad_storage: toConsentValue(selection.advertising),
    ad_user_data: toConsentValue(selection.advertising),
    // This product never requests personalized ads, even after ad consent.
    ad_personalization: "denied",
    analytics_storage: toConsentValue(selection.analytics),
  });
  getGoogleTag()("set", "ads_data_redaction", true);
  getGoogleTag()("set", "url_passthrough", false);
}

export function disableGoogleAnalytics(measurementId: string | null): void {
  if (!measurementId || parseGa4MeasurementId(measurementId) !== measurementId) {
    return;
  }
  const mutableWindow = window as unknown as Record<string, unknown>;
  mutableWindow[`ga-disable-${measurementId}`] = true;
}

export async function loadGoogleAnalytics(
  measurementId: string,
  nonce?: string,
): Promise<void> {
  if (parseGa4MeasurementId(measurementId) !== measurementId) {
    throw new Error("Invalid GA4 measurement ID.");
  }

  initializeGoogleConsentDefaults();
  await loadGoogleScript(
    `tarot-ga4-${measurementId}`,
    `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(measurementId)}`,
    nonce,
  );
}

export function configureGoogleAnalytics(measurementId: string): void {
  if (parseGa4MeasurementId(measurementId) !== measurementId) return;

  const state = getGoogleClientState();
  if (state.analyticsConfigured.has(measurementId)) return;

  const mutableWindow = window as unknown as Record<string, unknown>;
  mutableWindow[`ga-disable-${measurementId}`] = false;
  const gtag = getGoogleTag();
  gtag("js", new Date());
  gtag("config", measurementId, {
    send_page_view: false,
    allow_google_signals: false,
    allow_ad_personalization_signals: false,
    anonymize_ip: true,
  });
  state.analyticsConfigured.add(measurementId);
}

export function sendSanitizedGa4PageView(
  measurementId: string,
  pathname: string,
  locale: "en" | "th",
): void {
  if (parseGa4MeasurementId(measurementId) !== measurementId) return;

  const safePath = sanitizePathname(pathname);
  if (!safePath) return;

  const state = getGoogleClientState();
  const pageKey = `${locale}:${safePath}`;
  if (state.lastAnalyticsPage.get(measurementId) === pageKey) return;

  state.lastAnalyticsPage.set(measurementId, pageKey);
  getGoogleTag()("event", "page_view", {
    send_to: measurementId,
    page_location: `${window.location.origin}${safePath}`,
    page_path: safePath,
    language: locale,
  });
}

export function prepareNonPersonalizedAds(): void {
  const queue = getAdsenseQueue();
  queue.pauseAdRequests = 1;
  queue.requestNonPersonalizedAds = 1;
}

export async function loadAdsenseLibrary(
  clientId: string,
  nonce?: string,
): Promise<void> {
  if (parseAdsenseClientId(clientId) !== clientId) {
    throw new Error("Invalid AdSense client ID.");
  }

  initializeGoogleConsentDefaults();
  prepareNonPersonalizedAds();
  await loadGoogleScript(
    `tarot-adsense-${clientId}`,
    `https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=${encodeURIComponent(clientId)}`,
    nonce,
    (script) => {
      script.crossOrigin = "anonymous";
      script.dataset.privacyTreatments = "disablePersonalization";
    },
  );
}

export function initializeNonPersonalizedAd(element: HTMLElement): void {
  const state = getGoogleClientState();
  if (state.initializedAdElements.has(element)) return;

  const queue = getAdsenseQueue();
  queue.requestNonPersonalizedAds = 1;
  state.initializedAdElements.add(element);

  try {
    queue.push({
      params: { google_privacy_treatments: "disablePersonalization" },
    });
    queue.pauseAdRequests = 0;
  } catch (error) {
    state.initializedAdElements.delete(element);
    throw error;
  }
}

export function claimGuideAdPlacement(
  pathname: string,
  owner: symbol,
): boolean {
  const safePath = sanitizePathname(pathname);
  if (!safePath) return false;

  const claims = getGoogleClientState().activeAdPageClaims;
  const currentOwner = claims.get(safePath);
  if (currentOwner && currentOwner !== owner) return false;

  claims.set(safePath, owner);
  return true;
}

export function releaseGuideAdPlacement(
  pathname: string,
  owner: symbol,
): void {
  const safePath = sanitizePathname(pathname);
  if (!safePath) return;

  const claims = getGoogleClientState().activeAdPageClaims;
  if (claims.get(safePath) === owner) claims.delete(safePath);
}

export function queueGoogleCmpCallback(
  readiness:
    | "CONSENT_API_READY"
    | "CONSENT_DATA_READY"
    | "CONSENT_MODE_DATA_READY",
  callback: () => void,
): void {
  const googlefc = (window.googlefc ??= {});
  const queue = (googlefc.callbackQueue ??= []);
  queue.push({ [readiness]: callback });
}

export async function loadGoogleCmpBootstrap(
  publisherId: string,
  nonce?: string,
): Promise<void> {
  if (parseAdsensePublisherId(`ca-${publisherId}`) !== publisherId) {
    throw new Error("Invalid AdSense publisher ID.");
  }

  initializeGoogleConsentDefaults();
  installGoogleFcPresentFrame();
  await loadGoogleScript(
    `tarot-google-cmp-${publisherId}`,
    `https://fundingchoicesmessages.google.com/i/${encodeURIComponent(publisherId)}?ers=1`,
    nonce,
  );
}

export function readGoogleCmpConsent(): GoogleConsentSelection | null {
  const googlefc = window.googlefc;
  if (!googlefc?.getGoogleConsentModeValues) return null;

  try {
    const values = googlefc.getGoogleConsentModeValues();
    const granted = getGrantedEnumValue(
      googlefc.ConsentModePurposeStatusEnum,
    );

    return {
      analytics: values.analyticsStoragePurposeConsentStatus === granted,
      // ad_personalization is intentionally not required: every unit is NPA.
      advertising:
        values.adStoragePurposeConsentStatus === granted &&
        values.adUserDataPurposeConsentStatus === granted,
    };
  } catch {
    return null;
  }
}

export function showGoogleCmpRevocationMessage(): void {
  queueGoogleCmpCallback("CONSENT_API_READY", () => {
    window.googlefc?.showRevocationMessage?.();
  });
}

function toConsentValue(value: boolean): GoogleConsentValue {
  return value ? "granted" : "denied";
}

function getGoogleTag(): GoogleTagFunction {
  const dataLayer = (window.dataLayer ??= []);
  if (!window.gtag) {
    window.gtag = (...args: GoogleTagArguments) => {
      dataLayer.push(args);
    };
  }
  return window.gtag;
}

function getAdsenseQueue(): AdsByGoogleQueue {
  return (window.adsbygoogle ??= [] as unknown as AdsByGoogleQueue);
}

function getGoogleClientState(): GoogleClientState {
  return (window.__tarotGoogleClientState ??= {
    scriptLoads: new Map(),
    analyticsConfigured: new Set(),
    lastAnalyticsPage: new Map(),
    initializedAdElements: new WeakSet(),
    activeAdPageClaims: new Map(),
    consentDefaultsQueued: false,
  });
}

function loadGoogleScript(
  id: string,
  source: string,
  nonce?: string,
  configure?: (script: HTMLScriptElement) => void,
): Promise<void> {
  const state = getGoogleClientState();
  const pending = state.scriptLoads.get(id);
  if (pending) return pending;

  const existing = document.getElementById(id);
  if (existing instanceof HTMLScriptElement) {
    const completed = Promise.resolve();
    state.scriptLoads.set(id, completed);
    return completed;
  }

  const loading = new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.id = id;
    script.async = true;
    script.src = source;
    script.referrerPolicy = "strict-origin-when-cross-origin";
    script.dataset.googleConsentManaged = "true";
    if (nonce) script.nonce = nonce;
    configure?.(script);

    script.addEventListener("load", () => resolve(), { once: true });
    script.addEventListener(
      "error",
      () => {
        state.scriptLoads.delete(id);
        script.remove();
        reject(new Error(`Could not load Google script: ${id}`));
      },
      { once: true },
    );
    document.head.appendChild(script);
  });

  state.scriptLoads.set(id, loading);
  return loading;
}

function getGrantedEnumValue(
  values: GoogleConsentModePurposeStatusEnum | undefined,
): number {
  return (
    values?.GRANTED ??
    values?.CONSENT_MODE_PURPOSE_STATUS_GRANTED ??
    1
  );
}

function sanitizePathname(value: string): string | null {
  const pathname = value.trim();
  if (
    pathname.length === 0 ||
    pathname.length > 2048 ||
    !pathname.startsWith("/") ||
    pathname.includes("?") ||
    pathname.includes("#") ||
    /[\u0000-\u001f\u007f]/.test(pathname)
  ) {
    return null;
  }

  return pathname;
}

function installGoogleFcPresentFrame(): void {
  if (document.querySelector('iframe[name="googlefcPresent"]')) return;

  const iframe = document.createElement("iframe");
  iframe.name = "googlefcPresent";
  iframe.title = "";
  iframe.tabIndex = -1;
  iframe.setAttribute("aria-hidden", "true");
  iframe.style.cssText =
    "display:none;width:0;height:0;border:0;position:absolute;left:-1000px;top:-1000px";
  document.body.appendChild(iframe);
}
