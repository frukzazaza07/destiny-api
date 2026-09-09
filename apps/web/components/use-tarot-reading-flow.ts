"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api-client";
import { ReadingJobClient } from "../lib/reading-job-client";
import type { Locale } from "../lib/i18n";
import type { RewardedDeepStatus } from "./rewarded-deep-unlock";

export type QuestionMode = "TOPIC" | "QUESTION";
export type Spread = "DESTINY_3" | "DAILY_1";
export type Orientation = "UPRIGHT" | "REVERSED";
export type CacheStatus = "HIT" | "MISS" | "SKIPPED";
export type ReadingMode = "STANDARD" | "DEEP";
export type ShuffleVisualStep = "MIXING" | "SETTLING" | "DEALING";
export type ReadingPhase =
  | "IDLE"
  | "SHUFFLING"
  | "SELECTING"
  | "RESOLVING"
  | "GENERATING"
  | "REVEALING"
  | "COMPLETE"
  | "ERROR";
export type FailureStep = "SHUFFLING" | "RESOLVING" | "GENERATING";
export type TopicId = "GENERAL" | "LOVE" | "CAREER" | "MONEY" | "FAMILY" | "PERSONAL_GROWTH";

export type SelectedCard = {
  position: string;
  cardId: string;
  orientation: Orientation;
};

export type ShuffleResponse = {
  sessionId: string;
  spread: string;
  cardCount: number;
  selectCount: number;
};

export type ReadingResponse = {
  promptReadingId?: string | null;
  title: string;
  summary: string;
  mainTheme: string;
  cards: Array<SelectedCard & { cardName: string; interpretation: string }>;
  opportunities: string[];
  challenges: string[];
  guidance: string[];
  reflectionQuestion: string;
  closingMessage: string;
  cacheStatus: CacheStatus;
  classification: {
    domain: string;
    intent: string;
    confidence: number;
    personalization: string;
  };
  cacheKey: string | null;
  readingMode: ReadingMode;
  generationSource: "RULE_ENGINE" | "LLM";
  generationModel: string | null;
  modelTier: string | null;
  inferenceWorker: string | null;
  inferenceProvider: string | null;
  promptVariant: string | null;
  qualityScore: number | null;
};

export type ReadingOptions = {
  deepReading: {
    enabled: boolean;
    entitled: boolean;
    upgradeUrl: string | null;
    authenticated: boolean;
    premiumExpiresAt: string | null;
    availableAdEarnedCredits: number;
  };
  modelTiers: Array<{ id: string; model: string; available: boolean }>;
};

export type FlowMessages = {
  questionRequired: string;
  shuffleError: string;
  revealError: string;
  readingError: string;
  unavailableError: string;
};

const shuffleTiming = {
  minimumMix: 900,
  settle: 620,
  deal: 1700,
} as const;

export const tarotTopics: Array<{
  id: TopicId;
  label: Record<Locale, string>;
  request: Record<Locale, string>;
}> = [
  {
    id: "GENERAL",
    label: { en: "General", th: "ภาพรวม" },
    request: { en: "Give me general guidance for this period.", th: "ขอคำแนะนำภาพรวมสำหรับช่วงเวลานี้" },
  },
  {
    id: "LOVE",
    label: { en: "Love", th: "ความรัก" },
    request: { en: "Give me guidance about love and relationships.", th: "ขอคำแนะนำเกี่ยวกับความรักและความสัมพันธ์" },
  },
  {
    id: "CAREER",
    label: { en: "Career", th: "การงาน" },
    request: { en: "Give me guidance about work and my career path.", th: "ขอคำแนะนำเกี่ยวกับงานและเส้นทางอาชีพ" },
  },
  {
    id: "MONEY",
    label: { en: "Money", th: "การเงิน" },
    request: { en: "Give me guidance about money and finances.", th: "ขอคำแนะนำเกี่ยวกับการเงิน" },
  },
  {
    id: "FAMILY",
    label: { en: "Family", th: "ครอบครัว" },
    request: { en: "Give me guidance about family matters.", th: "ขอคำแนะนำเกี่ยวกับครอบครัว" },
  },
  {
    id: "PERSONAL_GROWTH",
    label: { en: "Personal Growth", th: "การพัฒนาตนเอง" },
    request: {
      en: "Give me guidance about personal growth and self-development.",
      th: "ขอคำแนะนำเกี่ยวกับการเติบโตและพัฒนาตนเอง",
    },
  },
];

const initialDeepAccess: ReadingOptions["deepReading"] = {
  enabled: false,
  entitled: false,
  upgradeUrl: null,
  authenticated: false,
  premiumExpiresAt: null,
  availableAdEarnedCredits: 0,
};

export function useTarotReadingFlow({
  locale,
  reduceMotion,
  messages,
}: {
  locale: Locale;
  reduceMotion: boolean;
  messages: FlowMessages;
}) {
  const [spread, setSpread] = useState<Spread>("DESTINY_3");
  const [questionMode, setQuestionMode] = useState<QuestionMode>("TOPIC");
  const [topic, setTopic] = useState<TopicId>("CAREER");
  const [question, setQuestion] = useState("");
  const [readingMode, setReadingMode] = useState<ReadingMode>("STANDARD");
  const [modelTier, setModelTier] = useState("CORE");
  const [modelTiers, setModelTiers] = useState<ReadingOptions["modelTiers"]>([]);
  const [deepAccess, setDeepAccess] = useState<ReadingOptions["deepReading"]>(initialDeepAccess);
  const [rewardUnlockAvailable, setRewardUnlockAvailable] = useState(false);
  const [shuffle, setShuffle] = useState<ShuffleResponse | null>(null);
  const [selected, setSelected] = useState<number[]>([]);
  const [cards, setCards] = useState<SelectedCard[]>([]);
  const [reading, setReading] = useState<ReadingResponse | null>(null);
  const [phase, setPhase] = useState<ReadingPhase>("IDLE");
  const [shuffleVisualStep, setShuffleVisualStep] = useState<ShuffleVisualStep>("MIXING");
  const [failureStep, setFailureStep] = useState<FailureStep | null>(null);
  const [error, setError] = useState<string | null>(null);
  const flowVersion = useRef(0);
  const activeRequest = useRef<AbortController | null>(null);
  const jobClient = useRef(new ReadingJobClient());

  useEffect(() => {
    try {
      const saved = sessionStorage.getItem(`tarot-reading:${locale}`);
      if (saved) {
        const value = JSON.parse(saved);
        if (value.reading?.promptReadingId && Array.isArray(value.reading.cards)) {
          setReading(value.reading); setCards(value.reading.cards);
          setSpread(value.spread); setQuestion(value.question); setQuestionMode(value.questionMode);
          setTopic(value.topic); setReadingMode(value.reading.readingMode); setPhase("COMPLETE");
        }
      }
    } catch { /* Browser storage is optional. */ }
  }, [locale]);

  const selectLimit = shuffle?.selectCount ?? (spread === "DAILY_1" ? 1 : 3);
  const selectedTopic = tarotTopics.find((item) => item.id === topic) ?? tarotTopics[0];
  const composedQuestion = questionMode === "TOPIC"
    ? `TOPIC: ${selectedTopic.id}\nREQUEST: ${selectedTopic.request[locale]}`
    : question.trim();
  const selectedSet = useMemo(() => new Set(selected), [selected]);
  const selectionOrder = useMemo(
    () => new Map(selected.map((index, order) => [index, order + 1])),
    [selected],
  );
  const isWorking = phase === "SHUFFLING" || phase === "RESOLVING" || phase === "GENERATING";
  const controlsLocked = isWorking || phase === "REVEALING";

  const cancelActiveFlow = useCallback(() => {
    flowVersion.current += 1;
    activeRequest.current?.abort();
    activeRequest.current = null;
  }, []);

  const resetReadingFlow = useCallback(() => {
    try { sessionStorage.removeItem(`tarot-reading:${locale}`); } catch { /* Storage may be disabled. */ }
    jobClient.current.cancel();
    cancelActiveFlow();
    setShuffle(null);
    setSelected([]);
    setCards([]);
    setReading(null);
    setPhase("IDLE");
    setShuffleVisualStep("MIXING");
    setFailureStep(null);
    setError(null);
  }, [cancelActiveFlow, locale]);

  useEffect(() => {
    const controller = new AbortController();
    apiFetch("/api/readings/options", { cache: "no-store", signal: controller.signal })
      .then((response) => (response.ok ? readApiData<ReadingOptions>(response) : null))
      .then((options) => {
        if (!options?.deepReading || controller.signal.aborted) return;
        setDeepAccess(options.deepReading);
        setModelTiers(options.modelTiers ?? []);
        const firstAvailable = options.modelTiers?.find((tier) => tier.available);
        if (firstAvailable) setModelTier(firstAvailable.id);
      })
      .catch(() => {
        // STANDARD remains available when capability discovery fails.
      });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (phase !== "REVEALING" || !reading) return;
    const version = flowVersion.current;
    const timer = window.setTimeout(() => {
      if (flowVersion.current === version) setPhase("COMPLETE");
    }, reduceMotion ? 0 : 850);
    return () => window.clearTimeout(timer);
  }, [phase, reading, reduceMotion]);

  useEffect(() => () => cancelActiveFlow(), [cancelActiveFlow]);

  const beginRequest = useCallback(() => {
    cancelActiveFlow();
    const controller = new AbortController();
    activeRequest.current = controller;
    return { controller, version: flowVersion.current };
  }, [cancelActiveFlow]);

  const isCurrent = useCallback((version: number) => flowVersion.current === version, []);

  const changeSpread = useCallback((nextSpread: Spread) => {
    setSpread(nextSpread);
    resetReadingFlow();
  }, [resetReadingFlow]);

  const changeQuestionMode = useCallback((nextMode: QuestionMode) => {
    setQuestionMode(nextMode);
    resetReadingFlow();
  }, [resetReadingFlow]);

  const changeTopic = useCallback((nextTopic: TopicId) => {
    setTopic(nextTopic);
    resetReadingFlow();
  }, [resetReadingFlow]);

  const changeQuestion = useCallback((nextQuestion: string) => {
    setQuestion(nextQuestion);
    resetReadingFlow();
  }, [resetReadingFlow]);

  const changeModelTier = useCallback((nextTier: string) => {
    setModelTier(nextTier);
    resetReadingFlow();
  }, [resetReadingFlow]);

  const changeReadingMode = useCallback((nextMode: ReadingMode) => {
    if (nextMode === "DEEP" && !deepAccess.entitled) return false;
    setReadingMode(nextMode);
    resetReadingFlow();
    return true;
  }, [deepAccess.entitled, resetReadingFlow]);

  const handleRewardStatus = useCallback((status: RewardedDeepStatus) => {
    setRewardUnlockAvailable(status.enabled);
    setDeepAccess((current) => ({
      ...current,
      entitled: current.premiumExpiresAt !== null || status.availableDeepCredits > 0,
      availableAdEarnedCredits: status.availableDeepCredits,
    }));
  }, []);

  const generateReading = useCallback(async (
    resolvedCards: SelectedCard[],
    currentShuffle: ShuffleResponse,
    controller: AbortController,
    version: number,
  ) => {
    setPhase("GENERATING");
    try {
      const request = {
        question: composedQuestion, spread: currentShuffle.spread, locale, readingMode,
        modelTier: readingMode === "DEEP" ? modelTier : null, cards: resolvedCards,
      };
      const generatedPromise = readingMode === "DEEP" && modelTier === "CLOUD"
        ? jobClient.current.generate<ReadingResponse>(request, controller.signal, messages.readingError)
        : apiFetch("/api/readings/generate", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          question: composedQuestion,
          spread: currentShuffle.spread,
          locale,
          readingMode,
          modelTier: readingMode === "DEEP" ? modelTier : null,
          cards: resolvedCards,
        }),
        signal: controller.signal,
      }).then(async generated => {
        if (!generated.ok) throw new Error(await readApiError(generated, messages.readingError));
        return readApiData<ReadingResponse>(generated);
      });
      const [generated] = await Promise.all([
        generatedPromise,
        waitFor(reduceMotion ? 0 : 300, controller.signal),
      ]);
      const nextReading = generated;
      if (!isCurrent(version)) return;
      activeRequest.current = null;
      setReading(nextReading);
      try {
        sessionStorage.setItem(`tarot-reading:${locale}`, JSON.stringify({ reading: nextReading,
          spread: currentShuffle.spread, question, questionMode, topic }));
      } catch { /* Reading can still be copied without browser persistence. */ }
      if (readingMode === "DEEP" && modelTier === "CLOUD") {
        void apiFetch("/api/readings/options", { cache: "no-store" })
          .then(response => readApiData<ReadingOptions>(response))
          .then(options => { if (isCurrent(version)) setDeepAccess(options.deepReading); }).catch(() => {});
      } else if (readingMode === "DEEP" && !deepAccess.premiumExpiresAt && deepAccess.availableAdEarnedCredits > 0) {
        const remaining = deepAccess.availableAdEarnedCredits - 1;
        setDeepAccess((current) => ({ ...current, availableAdEarnedCredits: remaining, entitled: remaining > 0 }));
      }
      setFailureStep(null);
      setPhase("REVEALING");
    } catch (cause) {
      if (isAbortError(cause) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("GENERATING");
      setError(getRequestError(cause, messages.readingError, messages.unavailableError));
      setPhase("ERROR");
    }
  }, [composedQuestion, deepAccess.availableAdEarnedCredits, deepAccess.premiumExpiresAt, isCurrent, locale, messages, modelTier, readingMode, reduceMotion, question, questionMode, topic]);

  const startShuffle = useCallback(async () => {
    if (activeRequest.current || controlsLocked) return;
    if (questionMode === "QUESTION" && !question.trim()) {
      setError(messages.questionRequired);
      setFailureStep(null);
      setPhase("ERROR");
      return;
    }
    const { controller, version } = beginRequest();
    setPhase("SHUFFLING");
    setShuffleVisualStep("MIXING");
    setFailureStep(null);
    setError(null);
    setShuffle(null);
    setReading(null);
    setCards([]);
    setSelected([]);
    try {
      const responsePromise = apiFetch("/api/deck/shuffle", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ spread }),
        signal: controller.signal,
      });
      const [response] = await Promise.all([
        responsePromise,
        waitFor(reduceMotion ? 0 : shuffleTiming.minimumMix, controller.signal),
      ]);
      if (!response.ok) throw new Error(await readApiError(response, messages.shuffleError));
      const nextShuffle = await readApiData<ShuffleResponse>(response);
      if (!isCurrent(version)) return;
      setShuffleVisualStep("SETTLING");
      await waitFor(reduceMotion ? 0 : shuffleTiming.settle, controller.signal);
      if (!isCurrent(version)) return;
      setShuffle(nextShuffle);
      setShuffleVisualStep("DEALING");
      await waitFor(reduceMotion ? 0 : shuffleTiming.deal, controller.signal);
      if (!isCurrent(version)) return;
      activeRequest.current = null;
      setPhase("SELECTING");
    } catch (cause) {
      if (isAbortError(cause) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("SHUFFLING");
      setError(getRequestError(cause, messages.shuffleError, messages.unavailableError));
      setPhase("ERROR");
    }
  }, [beginRequest, controlsLocked, isCurrent, messages, question, questionMode, reduceMotion, spread]);

  const toggleCard = useCallback((index: number) => {
    if (!shuffle || phase !== "SELECTING" || !Number.isInteger(index) || index < 0 || index >= shuffle.cardCount) return;
    setSelected((current) => {
      if (current.includes(index)) return current.filter((item) => item !== index);
      if (current.length >= selectLimit) return current;
      return [...current, index];
    });
  }, [phase, selectLimit, shuffle]);

  const revealAndRead = useCallback(async () => {
    if (!shuffle || selected.length !== selectLimit || activeRequest.current || (phase !== "SELECTING" && failureStep !== "RESOLVING")) return;
    const currentShuffle = shuffle;
    const { controller, version } = beginRequest();
    setPhase("RESOLVING");
    setFailureStep(null);
    setError(null);
    try {
      const resolved = await apiFetch(`/api/deck/${currentShuffle.sessionId}/resolve`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ selectedIndexes: selected }),
        signal: controller.signal,
      });
      if (!resolved.ok) throw new Error(await readApiError(resolved, messages.revealError));
      const resolvedBody = await readApiData<{ cards: SelectedCard[] }>(resolved);
      if (!isCurrent(version)) return;
      setCards(resolvedBody.cards);
      await generateReading(resolvedBody.cards, currentShuffle, controller, version);
    } catch (cause) {
      if (isAbortError(cause) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("RESOLVING");
      setError(getRequestError(cause, messages.revealError, messages.unavailableError));
      setPhase("ERROR");
    }
  }, [beginRequest, failureStep, generateReading, isCurrent, messages, phase, selectLimit, selected, shuffle]);

  const retryGeneration = useCallback(async () => {
    if (!shuffle || cards.length === 0 || activeRequest.current) return;
    const { controller, version } = beginRequest();
    setError(null);
    setFailureStep(null);
    await generateReading(cards, shuffle, controller, version);
  }, [beginRequest, cards, generateReading, shuffle]);

  const retryFailedStep = useCallback(() => {
    if (failureStep === "SHUFFLING") void startShuffle();
    if (failureStep === "RESOLVING") void revealAndRead();
    if (failureStep === "GENERATING") void retryGeneration();
  }, [failureStep, retryGeneration, revealAndRead, startShuffle]);

  return {
    spread,
    questionMode,
    topic,
    question,
    readingMode,
    modelTier,
    modelTiers,
    deepAccess,
    rewardUnlockAvailable,
    shuffle,
    selected,
    selectedSet,
    selectionOrder,
    cards,
    reading,
    phase,
    shuffleVisualStep,
    failureStep,
    error,
    selectLimit,
    isWorking,
    controlsLocked,
    changeSpread,
    changeQuestionMode,
    changeTopic,
    changeQuestion,
    changeReadingMode,
    changeModelTier,
    handleRewardStatus,
    startShuffle,
    toggleCard,
    revealAndRead,
    retryFailedStep,
    resetReadingFlow,
    cancelActiveFlow,
  };
}

export type TarotReadingFlow = ReturnType<typeof useTarotReadingFlow>;

function waitFor(milliseconds: number, signal: AbortSignal) {
  return new Promise<void>((resolve, reject) => {
    if (signal.aborted) {
      reject(new DOMException("The request was aborted.", "AbortError"));
      return;
    }
    const timer = window.setTimeout(() => {
      signal.removeEventListener("abort", handleAbort);
      resolve();
    }, milliseconds);
    const handleAbort = () => {
      window.clearTimeout(timer);
      reject(new DOMException("The request was aborted.", "AbortError"));
    };
    signal.addEventListener("abort", handleAbort, { once: true });
  });
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}

async function readApiError(response: Response, fallback: string) {
  try {
    const payload: unknown = await response.json();
    const messages = collectErrorMessages(payload);
    if (messages.length > 0) return `${fallback} ${messages.join(" ")}`;
  } catch {
    // Infrastructure errors can have an empty or non-JSON response body.
  }
  return `${fallback} (${response.status})`;
}

async function readApiData<T>(response: Response): Promise<T> {
  const payload: unknown = await response.json();
  if (isRecord(payload) && typeof payload.success === "boolean" && "data" in payload) {
    return payload.data as T;
  }
  return payload as T;
}

function collectErrorMessages(payload: unknown): string[] {
  if (typeof payload === "string" && payload.trim()) return [payload.trim()];
  if (Array.isArray(payload)) return payload.flatMap(collectErrorMessages);
  if (!payload || typeof payload !== "object") return [];
  const body = payload as Record<string, unknown>;
  const directMessages = [body.error, body.detail, body.message, body.title].flatMap(collectErrorMessages);
  const validationMessages = collectErrorMessages(body.errors);
  const messages = [...directMessages, ...validationMessages];
  return messages.length > 0
    ? [...new Set(messages)]
    : [...new Set(Object.values(body).flatMap(collectErrorMessages))];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function getRequestError(error: unknown, fallback: string, unavailable: string) {
  if (error instanceof TypeError) return unavailable;
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}
