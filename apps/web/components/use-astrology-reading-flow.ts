"use client";

import { useEffect, useRef, useState } from "react";
import { ReadingJobClient } from "../lib/reading-job-client";
import type { Locale } from "../lib/i18n";

export type AstrologyReading = {
  readingType: "THAI_ASTROLOGY"; locale: Locale; birthDate: string; birthTime: string | null; birthPlace: string | null;
  sections: { overview: string; analysis: string; directAnswer: string; timing: string | null; timingExplanation: string; advice: string; dataLimitations: string };
};

export function useAstrologyReadingFlow(locale: Locale) {
  const [birthDate, setBirthDate] = useState("");
  const [birthTime, setBirthTime] = useState("");
  const [birthPlace, setBirthPlace] = useState("");
  const [question, setQuestion] = useState("");
  const [state, setState] = useState("FORM");
  const [reading, setReading] = useState<AstrologyReading | null>(null);
  const [error, setError] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const client = useRef<ReadingJobClient | null>(null);
  if (!client.current) client.current = new ReadingJobClient('/api/reading-jobs/thai-astrology', 'astrologyReading', setState);
  useEffect(() => () => controller.current?.abort(), []);
  const busy = ["SUBMITTING", "QUEUED", "RUNNING"].includes(state);
  async function submit() {
    if (controller.current) return;
    const pending = new AbortController(); controller.current = pending;
    setState("SUBMITTING"); setError(false);
    try {
      const result = await client.current!.generate<AstrologyReading>({ readingType: "THAI_ASTROLOGY", birthDate,
        birthTime: birthTime || null, birthPlace: birthPlace.trim() || null, question: question.trim(), locale }, pending.signal, "ASTROLOGY_FAILED");
      if (!pending.signal.aborted) { setReading(result); setState("COMPLETED"); }
    } catch {
      if (!pending.signal.aborted) { setError(true); setState("FAILED"); }
    } finally { if (controller.current === pending) controller.current = null; }
  }
  function reset() {
    client.current!.cancel(); controller.current?.abort(); controller.current = null;
    setReading(null); setError(false); setState(busy ? "CANCELED" : "FORM");
  }
  return { birthDate, setBirthDate, birthTime, setBirthTime, birthPlace, setBirthPlace, question, setQuestion,
    state, reading, error, busy, submit, reset };
}
export type AstrologyFlow = ReturnType<typeof useAstrologyReadingFlow>;
