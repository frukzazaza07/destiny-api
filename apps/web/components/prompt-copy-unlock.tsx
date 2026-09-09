"use client";

import { useEffect, useRef, useState } from "react";
import { apiFetch, apiMutation, readApiData } from "../lib/api-client";
import { localizedPath, type Locale } from "../lib/i18n";
import { openConsentSettings } from "../lib/consent-preferences";
import { useConsent } from "./monetization";
import { rewardedAdCopy } from "./rewarded-deep-unlock";

type Status = { readingId: string; completedAds: number; requiredAds: number; unlocked: boolean; available: boolean };
type Attempt = { attemptId: string; expiresAt: string };

declare global {
  interface Window {
    // The provider adapter binds this attempt to its server-verified completion callback.
    // Resolving "completed" only starts polling; it never grants a reward in the browser.
    tarotPromptRewardedProvider?: (attempt: Attempt, signal: AbortSignal) => Promise<"completed" | "closed" | "no-fill" | "error">;
  }
}

const words = {
  en: {
    button: "Copy prompt for another AI fortune teller", login: "Log in to unlock this reading’s prompt",
    requirement: (n: number) => `Complete ${n} optional rewarded ads to copy this reading’s prompt.`,
    copied: "Prompt copied. Paste it into your chosen AI.", error: "Could not copy the prompt. Please try again.",
    fallback: "Clipboard access failed. Select and copy the prompt below.", unavailable: "Prompt unlocking is unavailable right now. Please try again later.",
    pending: "Waiting for the ad provider to confirm completion. You can return later; progress is saved.",
    cancel: "Cancel", terms: "Each ad is optional. The unlock applies to this reading, and you can copy it again afterward.",
  },
  th: {
    button: "คัดลอก prompt ถึงหมอดู AI อื่น", login: "เข้าสู่ระบบเพื่อปลดล็อก prompt ของการอ่านนี้",
    requirement: (n: number) => `ดูโฆษณาแบบให้รางวัลสำเร็จ ${n} รายการโดยสมัครใจ เพื่อคัดลอก prompt ของการอ่านนี้`,
    copied: "คัดลอก prompt แล้ว นำไปวางใน AI ที่คุณเลือกได้เลย", error: "คัดลอก prompt ไม่สำเร็จ โปรดลองอีกครั้ง",
    fallback: "เข้าถึงคลิปบอร์ดไม่ได้ โปรดเลือกและคัดลอกข้อความด้านล่าง", unavailable: "ขณะนี้ยังปลดล็อก prompt ไม่ได้ โปรดลองใหม่ภายหลัง",
    pending: "กำลังรอผู้ให้บริการโฆษณายืนยัน คุณกลับมาภายหลังได้ ความคืบหน้าถูกบันทึกแล้ว",
    cancel: "ยกเลิก", terms: "โฆษณาแต่ละรายการเป็นทางเลือก การปลดล็อกใช้กับการอ่านนี้ และคัดลอกซ้ำได้ภายหลัง",
  },
} as const;

export default function PromptCopyUnlock({ locale, readingId }: { locale: Locale; readingId?: string | null }) {
  const text = words[locale];
  const adText = rewardedAdCopy[locale];
  const consent = useConsent();
  const [status, setStatus] = useState<Status | null>(null);
  const [login, setLogin] = useState(false);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [fallback, setFallback] = useState("");
  const active = useRef<AbortController | null>(null);
  const path = `/api/readings/${readingId}/prompt`;

  useEffect(() => {
    const controller = new AbortController();
    if (readingId) void apiFetch(`${path}/status`, { cache: "no-store", signal: controller.signal })
      .then(async response => {
        if (response.status === 401) setLogin(true);
        else if (response.ok) setStatus(await readApiData<Status>(response));
      }).catch(() => {});
    return () => { controller.abort(); active.current?.abort(); };
  }, [path, readingId]);

  useEffect(() => {
    if (!open || !readingId || status?.unlocked) return;
    const controller = new AbortController();
    let polling = false;
    const refresh = async () => {
      if (polling) return;
      polling = true;
      try {
        const response = await apiFetch(`${path}/status`, { cache: "no-store", signal: controller.signal });
        if (response.status === 401) setLogin(true);
        else if (response.ok) { setStatus(await readApiData<Status>(response)); setLogin(false); }
      } catch { /* Retry on the next poll or browser focus. */ }
      finally { polling = false; }
    };
    const timer = setInterval(() => void refresh(), 5000);
    window.addEventListener("focus", refresh);
    return () => { controller.abort(); clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, [open, path, readingId, status?.unlocked]);

  async function copyPrompt() {
    if (active.current) return;
    const controller = new AbortController(); active.current = controller;
    setBusy(true); setFallback("");
    try {
      const response = await apiMutation(`${path}/copy`, "POST");
      if (response.status === 401) { setLogin(true); return; }
      if (!response.ok) throw new Error(text.error);
      const { prompt } = await readApiData<{ prompt: string }>(response);
      if (controller.signal.aborted) return;
      try { await navigator.clipboard.writeText(prompt); setMessage(text.copied); }
      catch { setFallback(prompt); setMessage(text.fallback); }
    } catch { if (!controller.signal.aborted) setMessage(text.error); }
    finally { active.current = null; setBusy(false); }
  }

  async function watch() {
    if (active.current) return;
    if (!consent.ready || !consent.advertising) { setMessage(adText.consent); openConsentSettings(); return; }
    if (!window.tarotPromptRewardedProvider) { setMessage(text.unavailable); return; }
    const controller = new AbortController(); active.current = controller;
    setBusy(true); setMessage(adText.loading);
    let attempt: Attempt | null = null;
    let completed = false;
    try {
      const session = await apiMutation(`${path}/sessions`, "POST");
      if (session.status === 401) { setLogin(true); return; }
      if (!session.ok) throw new Error();
      const current = await readApiData<Status>(session); setStatus(current);
      if (current.unlocked || controller.signal.aborted) return;
      const response = await apiMutation(`${path}/attempts`, "POST");
      if (!response.ok) throw new Error();
      attempt = await readApiData<Attempt>(response);
      if (controller.signal.aborted) return;
      const outcome = await window.tarotPromptRewardedProvider(attempt, controller.signal);
      completed = outcome === "completed";
      if (!completed) { setMessage(outcome === "closed" ? adText.close : outcome === "no-fill" ? adText.noFill : adText.error); return; }
      setMessage(text.pending);
      for (let poll = 0; poll < 15 && !controller.signal.aborted; poll++) {
        const response = await apiFetch(`${path}/status`, { cache: "no-store", signal: controller.signal });
        if (!response.ok) throw new Error();
        const next = await readApiData<Status>(response); setStatus(next);
        if (next.completedAds > current.completedAds || next.unlocked) {
          setMessage(adText.progress(next.completedAds, next.requiredAds)); return;
        }
        await new Promise<void>(resolve => {
          const done = () => { clearTimeout(timer); controller.signal.removeEventListener("abort", done); resolve(); };
          const timer = setTimeout(done, 2000);
          controller.signal.addEventListener("abort", done, { once: true });
        });
      }
    } catch { if (!controller.signal.aborted) setMessage(adText.error); }
    finally {
      if (attempt && !completed) await apiMutation(`${path}/attempts/${attempt.attemptId}/close`, "POST").catch(() => {});
      active.current = null; setBusy(false);
    }
  }

  return <section className="rewarded-unlock" aria-label={text.button}>
    <button className="secondary" type="button" disabled={busy} onClick={() => {
      setOpen(true);
      if (status?.unlocked) void copyPrompt();
    }}>{text.button}</button>
    {open && <>
      {login ? <a href={`${localizedPath(locale, "/login")}?return=${encodeURIComponent(window.location.pathname + window.location.search)}`}>{text.login}</a>
        : !status ? <p>{text.unavailable}</p>
        : !status.unlocked ? <>
          <p>{text.requirement(status.requiredAds)}</p>
          <p>{adText.progress(status.completedAds, status.requiredAds)}</p>
          {status.available ? <button type="button" disabled={busy} onClick={() => void watch()}>{busy ? adText.loading : adText.watch}</button> : <p>{text.unavailable}</p>}
          <small>{text.terms}</small>
        </> : null}
      {busy && <button type="button" onClick={() => active.current?.abort()}>{text.cancel}</button>}
      {message && <p role="status">{message}</p>}
      {fallback && <textarea aria-label={text.button} readOnly value={fallback} rows={16} onFocus={event => event.currentTarget.select()} style={{ width: "100%" }} />}
    </>}
  </section>;
}
