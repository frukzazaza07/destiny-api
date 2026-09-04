"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";

import {
  isMonetizableGuidePath,
  parseGa4MeasurementId,
} from "../../lib/monetization-validation";
import { useConsent } from "./consent-provider";
import {
  configureGoogleAnalytics,
  disableGoogleAnalytics,
  loadGoogleAnalytics,
  sendSanitizedGa4PageView,
  updateGoogleConsent,
} from "./google-browser";

export interface GoogleAnalyticsPageViewProps {
  /**
   * An explicit route allow-list result. Keep false for reading, admin, legal,
   * draft, preview, and unknown routes.
   */
  readonly eligibleGuidePage: boolean;
  readonly enabled: boolean;
  readonly measurementId: string | null;
  readonly locale: "en" | "th";
  readonly nonce?: string;
}

/** Sends only a sanitized guide pathname and locale as a GA4 page_view. */
export function GoogleAnalyticsPageView({
  eligibleGuidePage,
  enabled,
  measurementId,
  locale,
  nonce,
}: GoogleAnalyticsPageViewProps) {
  const pathname = usePathname();
  const consent = useConsent();

  useEffect(() => {
    const validMeasurementId = measurementId
      ? parseGa4MeasurementId(measurementId)
      : null;
    const mayLoad =
      eligibleGuidePage &&
      isMonetizableGuidePath(pathname, locale) &&
      enabled &&
      consent.ready &&
      consent.analytics &&
      consent.region !== "restricted" &&
      validMeasurementId !== null &&
      validMeasurementId === measurementId;

    if (!mayLoad || !measurementId) {
      disableGoogleAnalytics(validMeasurementId);
      return;
    }

    let active = true;
    updateGoogleConsent(consent);
    void loadGoogleAnalytics(measurementId, nonce)
      .then(() => {
        if (!active) return;
        configureGoogleAnalytics(measurementId);
        sendSanitizedGa4PageView(measurementId, pathname, locale);
      })
      .catch(() => {
        // Analytics is optional. Script failures never affect page rendering.
      });

    return () => {
      active = false;
    };
  }, [
    consent,
    eligibleGuidePage,
    enabled,
    locale,
    measurementId,
    nonce,
    pathname,
  ]);

  return null;
}
