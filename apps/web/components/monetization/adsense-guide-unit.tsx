"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname } from "next/navigation";

import {
  isMonetizableGuidePath,
  parseAdsenseClientId,
  parseAdsenseSlotId,
} from "../../lib/monetization-validation";
import { useConsent } from "./consent-provider";
import {
  claimGuideAdPlacement,
  initializeNonPersonalizedAd,
  loadAdsenseLibrary,
  prepareNonPersonalizedAds,
  releaseGuideAdPlacement,
  updateGoogleConsent,
} from "./google-browser";

export interface AdsenseGuideUnitProps {
  /** Must be true only for a reviewed, published guide route. */
  readonly eligibleGuidePage: boolean;
  readonly enabled: boolean;
  readonly clientId: string | null;
  readonly slotId: string | null;
  readonly locale: "en" | "th";
  readonly nonce?: string;
  readonly className?: string;
}

/**
 * Renders at most one responsive, non-personalized AdSense request per mounted
 * guide pathname. Invalid or disabled configuration renders no container.
 */
export function AdsenseGuideUnit({
  eligibleGuidePage,
  enabled,
  clientId,
  slotId,
  locale,
  nonce,
  className,
}: AdsenseGuideUnitProps) {
  const pathname = usePathname();
  const consent = useConsent();
  const elementRef = useRef<HTMLModElement>(null);
  const ownerRef = useRef(Symbol("guide-ad-placement"));
  const [claimed, setClaimed] = useState(false);
  const [failed, setFailed] = useState(false);

  const validClientId = clientId ? parseAdsenseClientId(clientId) : null;
  const validSlotId = slotId ? parseAdsenseSlotId(slotId) : null;
  const mayRequestAd =
    eligibleGuidePage &&
    isMonetizableGuidePath(pathname, locale) &&
    enabled &&
    consent.ready &&
    consent.advertising &&
    consent.region !== "restricted" &&
    validClientId !== null &&
    validSlotId !== null &&
    validClientId === clientId &&
    validSlotId === slotId;

  useEffect(() => {
    if (!mayRequestAd) {
      setClaimed(false);
      setFailed(false);
      return;
    }

    const owner = ownerRef.current;
    const acquired = claimGuideAdPlacement(pathname, owner);
    setClaimed(acquired);

    return () => {
      releaseGuideAdPlacement(pathname, owner);
    };
  }, [mayRequestAd, pathname]);

  useEffect(() => {
    const element = elementRef.current;
    if (!mayRequestAd || !claimed || !element || !clientId) return;

    let active = true;
    setFailed(false);
    updateGoogleConsent(consent);
    prepareNonPersonalizedAds();
    void loadAdsenseLibrary(clientId, nonce)
      .then(() => {
        if (!active || !element.isConnected) return;
        initializeNonPersonalizedAd(element);
      })
      .catch(() => {
        if (active) setFailed(true);
      });

    return () => {
      active = false;
    };
  }, [claimed, clientId, consent, mayRequestAd, nonce]);

  if (
    !mayRequestAd ||
    !claimed ||
    failed ||
    !validClientId ||
    !validSlotId
  ) {
    return null;
  }

  const label = locale === "th" ? "โฆษณา" : "Advertisement";

  return (
    <aside
      aria-label={label}
      className={className}
      style={{
        clear: "both",
        minHeight: "280px",
        margin: "3rem 0",
        padding: "1rem 0",
        borderBlock: "1px solid rgba(127, 113, 147, 0.22)",
        textAlign: "center",
      }}
    >
      <small
        style={{
          display: "block",
          marginBottom: "0.5rem",
          color: "#6b6275",
          fontSize: "0.72rem",
          letterSpacing: "0.08em",
          textTransform: "uppercase",
        }}
      >
        {label}
      </small>
      <ins
        className="adsbygoogle"
        data-ad-client={validClientId}
        data-ad-format="auto"
        data-ad-slot={validSlotId}
        data-full-width-responsive="true"
        ref={elementRef}
        style={{ display: "block", minHeight: "250px" }}
      />
    </aside>
  );
}
