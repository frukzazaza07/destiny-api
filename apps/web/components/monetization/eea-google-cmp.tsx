"use client";

import { useEffect } from "react";
import { usePathname } from "next/navigation";

import { CONSENT_SETTINGS_EVENT } from "../../lib/consent-preferences";
import {
  isMonetizableGuidePath,
  parseAdsensePublisherId,
} from "../../lib/monetization-validation";
import { useConsent } from "./consent-provider";
import {
  loadGoogleCmpBootstrap,
  queueGoogleCmpCallback,
  readGoogleCmpConsent,
  showGoogleCmpRevocationMessage,
} from "./google-browser";

export interface EeaGoogleCmpProps {
  /** Must only be true on routes permitted to load the Google CMP bootstrap. */
  readonly eligibleGuidePage: boolean;
  readonly enabled: boolean;
  readonly publisherId: string | null;
  readonly nonce?: string;
}

/**
 * Bridges Google's certified Privacy & Messaging CMP to the local basic
 * consent controller. It loads only the Funding Choices bootstrap; analytics
 * and AdSense remain blocked until the CMP reports affirmative consent.
 */
export function EeaGoogleCmp({
  eligibleGuidePage,
  enabled,
  publisherId,
  nonce,
}: EeaGoogleCmpProps) {
  const pathname = usePathname();
  const { region, reportGoogleCmpConsent } = useConsent();

  useEffect(() => {
    const validPublisherId = publisherId
      ? parseAdsensePublisherId(`ca-${publisherId}`)
      : null;
    if (
      region !== "eea" ||
      !eligibleGuidePage ||
      !isMonetizableGuidePath(pathname) ||
      !enabled ||
      !publisherId ||
      validPublisherId !== publisherId
    ) {
      return;
    }

    let active = true;
    const reportCurrentConsent = () => {
      if (!active) return;
      reportGoogleCmpConsent(readGoogleCmpConsent());
    };
    const reopenSettings = () => showGoogleCmpRevocationMessage();

    // Google's documented basic-consent hook fires only after a prior choice
    // is known or the user completes the three-choice CMP flow.
    queueGoogleCmpCallback(
      "CONSENT_MODE_DATA_READY",
      reportCurrentConsent,
    );
    window.addEventListener(CONSENT_SETTINGS_EVENT, reopenSettings);

    void loadGoogleCmpBootstrap(publisherId, nonce).catch(() => {
      if (active) reportGoogleCmpConsent(null);
    });

    return () => {
      active = false;
      window.removeEventListener(CONSENT_SETTINGS_EVENT, reopenSettings);
    };
  }, [
    eligibleGuidePage,
    enabled,
    nonce,
    pathname,
    publisherId,
    region,
    reportGoogleCmpConsent,
  ]);

  return null;
}
