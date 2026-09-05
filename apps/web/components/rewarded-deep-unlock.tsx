"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch, apiMutation, readApiData, readApiError } from "../lib/api-client";
import type { Locale } from "../lib/i18n";
import { openConsentSettings } from "../lib/consent-preferences";
import { useConsent } from "./monetization";

export type RewardedDeepStatus = {
  enabled: boolean;
  serviceAvailable: boolean;
  provider: string;
  adUnitPath: string | null;
  validAdCompletions: number;
  requiredAdCompletions: number;
  deepCreditsPerCompletedBundle: number;
  availableDeepCredits: number;
  expiresAt: string | null;
  nextEligibleAt: string | null;
};

type Attempt = { nonce: string; expiresAt: string };
type ProviderOutcome = "granted" | "closed" | "no-fill" | "error";

declare global {
  interface Window {
    googletag?: GoogleTag;
    __tarotRewardedTestProvider?: () => Promise<ProviderOutcome>;
  }
}

type GoogleTag = {
  cmd: Array<() => void>;
  OutOfPageFormat: { REWARDED: string };
  defineOutOfPageSlot: (path: string, format: string) => GoogleSlot | null;
  pubads: () => GooglePubAds;
  enableServices: () => void;
  display: (slot: GoogleSlot) => void;
  destroySlots: (slots: GoogleSlot[]) => boolean;
};
type GoogleSlot = { addService: (service: GooglePubAds) => GoogleSlot };
type GoogleEvent = { slot: GoogleSlot; isEmpty?: boolean; makeRewardedVisible?: () => void };
type GooglePubAds = {
  addEventListener: (name: string, listener: (event: GoogleEvent) => void) => void;
  removeEventListener: (name: string, listener: (event: GoogleEvent) => void) => void;
};

const copy = {
  en: {
    label: "Optional rewarded advertising",
    title: (ads: number, credits: number) => `Watch ${ads} optional rewarded ${ads === 1 ? "ad" : "ads"} to unlock ${credits === 1 ? "one" : credits} DEEP ${credits === 1 ? "reading" : "readings"}.`,
    progress: (done: number, total: number) => `${done} of ${total} rewarded ads completed`,
    credits: (count: number) => `${count} DEEP ${count === 1 ? "credit" : "credits"} available`,
    watch: "Watch next optional ad",
    loading: "Preparing the rewarded ad…",
    consent: "Allow advertising in Privacy choices before requesting a rewarded ad.",
    choices: "Open Privacy choices",
    close: "You closed or declined the ad. Progress was not changed.",
    noFill: "No rewarded ad is available right now. Try again later.",
    error: "The rewarded ad could not load. Progress was not changed.",
    ready: "Your DEEP credit is ready.",
    limit: "One reward bundle is available per rolling 24 hours.",
    terms: "Each ad is a separate choice. Never click an ad for a reward. Credits expire after 24 hours, have no cash value, and cannot be transferred. STANDARD remains free.",
  },
  th: {
    label: "โฆษณาแบบให้รางวัล (ไม่บังคับ)",
    title: (ads: number, credits: number) => `ดูโฆษณาแบบให้รางวัล ${ads} รายการโดยสมัครใจ เพื่อปลดล็อกการอ่าน DEEP ${credits} ครั้ง`,
    progress: (done: number, total: number) => `ดูโฆษณาสำเร็จ ${done} จาก ${total} รายการ`,
    credits: (count: number) => `มีเครดิต DEEP ${count} เครดิต`,
    watch: "ดูโฆษณารายการถัดไป",
    loading: "กำลังเตรียมโฆษณาแบบให้รางวัล…",
    consent: "โปรดอนุญาตโฆษณาในการตั้งค่าความเป็นส่วนตัวก่อนขอโฆษณาแบบให้รางวัล",
    choices: "เปิดตัวเลือกความเป็นส่วนตัว",
    close: "คุณปิดหรือปฏิเสธโฆษณา ความคืบหน้าไม่เปลี่ยนแปลง",
    noFill: "ขณะนี้ไม่มีโฆษณาแบบให้รางวัล โปรดลองใหม่ภายหลัง",
    error: "โหลดโฆษณาแบบให้รางวัลไม่ได้ ความคืบหน้าไม่เปลี่ยนแปลง",
    ready: "เครดิต DEEP ของคุณพร้อมใช้งานแล้ว",
    limit: "รับรางวัลได้หนึ่งชุดต่อช่วงเวลา 24 ชั่วโมง",
    terms: "โฆษณาแต่ละรายการเป็นทางเลือกแยกกัน ห้ามคลิกโฆษณาเพื่อรับรางวัล เครดิตหมดอายุใน 24 ชั่วโมง ไม่มีมูลค่าเงินสด และโอนไม่ได้ การอ่าน STANDARD ยังใช้ฟรี",
  },
} as const;

export default function RewardedDeepUnlock({
  locale,
  premiumEntitled,
  onStatus,
}: {
  locale: Locale;
  premiumEntitled: boolean;
  onStatus: (status: RewardedDeepStatus) => void;
}) {
  const consent = useConsent();
  const [status, setStatus] = useState<RewardedDeepStatus | null>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const activeAttempt = useRef(false);
  const text = copy[locale];

  const publish = useCallback((next: RewardedDeepStatus) => {
    setStatus(next);
    onStatus(next);
  }, [onStatus]);

  useEffect(() => {
    let active = true;
    apiFetch("/api/rewards/deep/status", { cache: "no-store" })
      .then((response) => response.ok ? readApiData<RewardedDeepStatus>(response) : null)
      .then((next) => { if (active && next) publish(next); })
      .catch(() => undefined);
    return () => { active = false; };
  }, [publish]);

  async function watchNext() {
    if (busy || activeAttempt.current || !status) return;
    if (!consent.ready || !consent.advertising) {
      setMessage(text.consent);
      openConsentSettings();
      return;
    }

    setBusy(true);
    setMessage(text.loading);
    activeAttempt.current = true;
    try {
      let current = status;
      if (!current.expiresAt) {
        const sessionResponse = await apiMutation("/api/rewards/deep/sessions", "POST");
        if (!sessionResponse.ok) throw new Error(await readApiError(sessionResponse, text.error));
        current = await readApiData<RewardedDeepStatus>(sessionResponse);
        publish(current);
      }

      const attemptResponse = await apiMutation("/api/rewards/deep/attempts", "POST");
      if (!attemptResponse.ok) throw new Error(await readApiError(attemptResponse, text.error));
      const attempt = await readApiData<Attempt>(attemptResponse);
      const outcome = window.__tarotRewardedTestProvider
        ? await window.__tarotRewardedTestProvider()
        : await showGoogleRewardedAd(current.adUnitPath!);

      if (outcome === "granted") {
        const grantResponse = await apiMutation("/api/rewards/deep/grants", "POST", {
          nonce: attempt.nonce,
          eventType: "rewardedSlotGranted",
        });
        if (!grantResponse.ok) throw new Error(await readApiError(grantResponse, text.error));
        const next = await readApiData<RewardedDeepStatus>(grantResponse);
        publish(next);
        setMessage(next.availableDeepCredits > 0 ? text.ready : text.progress(next.validAdCompletions, next.requiredAdCompletions));
      } else {
        await apiMutation("/api/rewards/deep/attempts/close", "POST", { nonce: attempt.nonce });
        setMessage(outcome === "closed" ? text.close : outcome === "no-fill" ? text.noFill : text.error);
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : text.error);
    } finally {
      activeAttempt.current = false;
      setBusy(false);
    }
  }

  if (!status?.enabled || premiumEntitled) return null;
  const capped = status.nextEligibleAt !== null && status.validAdCompletions >= status.requiredAdCompletions;

  return (
    <section className="rewarded-unlock" id="rewarded-deep-unlock" aria-labelledby="rewarded-deep-heading">
      <p className="eyebrow">{text.label}</p>
      <h3 id="rewarded-deep-heading" tabIndex={-1}>{text.title(status.requiredAdCompletions, status.deepCreditsPerCompletedBundle)}</h3>
      <p className="reward-progress">{text.progress(status.validAdCompletions, status.requiredAdCompletions)}</p>
      {status.availableDeepCredits > 0 ? <p className="reward-credit" role="status">{text.credits(status.availableDeepCredits)}</p> : null}
      <button type="button" className="secondary" onClick={() => void watchNext()} disabled={busy || capped || status.availableDeepCredits > 0}>
        {busy ? text.loading : text.watch}
      </button>
      {capped ? <p>{text.limit}</p> : null}
      {message ? <p className="reward-message" role="status" aria-live="polite">{message}</p> : null}
      {!consent.advertising ? <button type="button" className="text-button" onClick={openConsentSettings}>{text.choices}</button> : null}
      <small>{text.terms}</small>
    </section>
  );
}

let gptLoad: Promise<void> | null = null;
function loadGooglePublisherTag() {
  if (window.googletag) return Promise.resolve();
  if (gptLoad) return gptLoad;
  gptLoad = new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.async = true;
    script.src = "https://securepubads.g.doubleclick.net/tag/js/gpt.js";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Rewarded ad provider failed to load."));
    document.head.appendChild(script);
  });
  return gptLoad;
}

async function showGoogleRewardedAd(adUnitPath: string): Promise<ProviderOutcome> {
  await loadGooglePublisherTag();
  return new Promise((resolve) => {
    const googleTag = window.googletag!;
    googleTag.cmd.push(() => {
      const slot = googleTag.defineOutOfPageSlot(adUnitPath, googleTag.OutOfPageFormat.REWARDED);
      if (!slot) { resolve("error"); return; }
      const pubads = googleTag.pubads();
      let granted = false;
      let settled = false;
      const finish = (outcome: ProviderOutcome) => {
        if (settled) return;
        settled = true;
        for (const [name, listener] of listeners) pubads.removeEventListener(name, listener);
        googleTag.destroySlots([slot]);
        resolve(outcome);
      };
      const ready = (event: GoogleEvent) => { if (event.slot === slot) event.makeRewardedVisible?.(); };
      const grant = (event: GoogleEvent) => { if (event.slot === slot) granted = true; };
      const close = (event: GoogleEvent) => { if (event.slot === slot) finish(granted ? "granted" : "closed"); };
      const rendered = (event: GoogleEvent) => { if (event.slot === slot && event.isEmpty) finish("no-fill"); };
      const listeners: Array<[string, (event: GoogleEvent) => void]> = [
        ["rewardedSlotReady", ready], ["rewardedSlotGranted", grant], ["rewardedSlotClosed", close], ["slotRenderEnded", rendered],
      ];
      for (const [name, listener] of listeners) pubads.addEventListener(name, listener);
      slot.addService(pubads);
      googleTag.enableServices();
      googleTag.display(slot);
    });
  });
}
