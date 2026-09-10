"use client";

import { useCallback, useEffect, useState } from "react";
import { astrologyCopy, type Locale } from "../lib/i18n";
import { apiFetch, readApiData } from "../lib/api-client";
import type { AstrologyFlow } from "./use-astrology-reading-flow";
import RewardedDeepUnlock, { type RewardedDeepStatus } from "./rewarded-deep-unlock";

export default function AstrologyConsultation({ locale, flow }: { locale: Locale; flow: AstrologyFlow }) {
  const text = astrologyCopy[locale];
  const [premium, setPremium] = useState(false);
  const [credits, setCredits] = useState(0);
  const onStatus = useCallback((status: RewardedDeepStatus) => setCredits(status.availableDeepCredits ?? 0), []);
  useEffect(() => {
    const abort = new AbortController();
    void apiFetch('/api/readings/options', { signal: abort.signal }).then(response => readApiData<{ deepReading: { entitled: boolean; availableAdEarnedCredits: number } }>(response))
      .then(options => { setPremium(options.deepReading.entitled); setCredits(options.deepReading.availableAdEarnedCredits); }).catch(() => {});
    return () => abort.abort();
  }, []);
  const resultText = astrologyCopy[flow.reading?.locale ?? locale];
  return <section className="astrology-consultation" aria-label={text.title}>
    <h2>{text.title}</h2><p>{text.intro}</p>
    <p>{text.privacy}</p>
    {!flow.reading && <>
      <RewardedDeepUnlock locale={locale} premiumEntitled={premium} onStatus={onStatus} />
      {!premium && credits === 0 && <p>{text.access}</p>}
      <form onSubmit={event => { event.preventDefault(); void flow.submit(); }}>
        <fieldset disabled={flow.busy}>
          <legend>{text.details}</legend>
          <label>{text.birthDate}<input type="date" required value={flow.birthDate} max={new Date().toISOString().slice(0, 10)} onChange={event => flow.setBirthDate(event.target.value)} /></label>
          <label>{text.birthTime}<input type="time" value={flow.birthTime} onChange={event => flow.setBirthTime(event.target.value)} /></label>
          <button type="button" onClick={() => flow.setBirthTime("")}>{text.unknown}</button>
          <label>{text.birthPlace}<input maxLength={200} value={flow.birthPlace} onChange={event => flow.setBirthPlace(event.target.value)} /></label>
          <label>{text.question}<textarea required maxLength={2000} value={flow.question} onChange={event => flow.setQuestion(event.target.value)} /></label>
          <button className="shop-primary" type="submit" disabled={!flow.question.trim()}>{flow.error ? text.retry : text.submit}</button>
        </fieldset>
      </form>
    </>}
    <p role="status" aria-live="polite">{text.states[flow.state as keyof typeof text.states] ?? ""}</p>
    {flow.error && <p role="alert">{text.error}</p>}
    {flow.busy && <button type="button" onClick={flow.reset}>{text.cancel}</button>}
    {flow.reading && <article lang={flow.reading.locale}>
      {(["overview", "analysis", "directAnswer", "timing", "advice", "dataLimitations"] as const).map(key => <section key={key}>
        <h3>{resultText.sections[key]}</h3><p>{key === "timing" ? flow.reading!.sections.timing ?? flow.reading!.sections.timingExplanation : flow.reading!.sections[key]}</p>
      </section>)}
      <button type="button" onClick={flow.reset}>{text.restart}</button>
    </article>}
  </section>;
}
