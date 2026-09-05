"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { BookOpen, RefreshCw, Settings, Sparkles } from "lucide-react";
import { useRouter } from "next/navigation";
import type { Locale } from "../lib/i18n";
import RewardedDeepUnlock, { type RewardedDeepStatus } from "./rewarded-deep-unlock";

type QuestionMode = "TOPIC" | "QUESTION";
type Spread = "DESTINY_3" | "DAILY_1";
type Orientation = "UPRIGHT" | "REVERSED";
type CacheStatus = "HIT" | "MISS" | "SKIPPED";
type ReadingMode = "STANDARD" | "DEEP";
type ShuffleVisualStep = "MIXING" | "SETTLING" | "DEALING";
type ReadingPhase =
  | "IDLE"
  | "SHUFFLING"
  | "SELECTING"
  | "RESOLVING"
  | "GENERATING"
  | "REVEALING"
  | "COMPLETE"
  | "ERROR";
type FailureStep = "SHUFFLING" | "RESOLVING" | "GENERATING";
type TopicId = "GENERAL" | "LOVE" | "CAREER" | "MONEY" | "FAMILY" | "PERSONAL_GROWTH";

type SelectedCard = {
  position: string;
  cardId: string;
  orientation: Orientation;
};

type ShuffleResponse = {
  sessionId: string;
  spread: string;
  cardCount: number;
  selectCount: number;
};

type ReadingResponse = {
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

type ReadingOptions = {
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

type UiCopy = {
  headline: string;
  language: string;
  spread: string;
  destinySpread: string;
  dailySpread: string;
  focus: string;
  chooseTopic: string;
  askQuestion: string;
  topic: string;
  question: string;
  questionPlaceholder: string;
  questionRequired: string;
  shuffle: string;
  shuffleAgain: string;
  reveal: string;
  awaitingShuffle: string;
  selected: string;
  deckReady: string;
  deckLabel: string;
  cardLabel: string;
  selectedOrder: string;
  selectingStatus: string;
  shufflingStatus: string;
  settlingStatus: string;
  dealingStatus: string;
  resolvingStatus: string;
  generatingStatus: string;
  deepGeneratingStatus: string;
  revealingStatus: string;
  readingReady: string;
  retryShuffle: string;
  retryReveal: string;
  retryReading: string;
  mainTheme: string;
  opportunities: string;
  challenges: string;
  guidance: string;
  shuffleError: string;
  revealError: string;
  readingError: string;
  unavailableError: string;
  readingStyle: string;
  standardReading: string;
  deepReading: string;
  premium: string;
  premiumRequired: string;
};

const isDev = process.env.NODE_ENV !== "production";
const shuffleTiming = {
  minimumMix: 900,
  settle: 620,
  deal: 1700
} as const;

const copy: Record<Locale, UiCopy> = {
  en: {
    headline: "Choose, reveal, reflect.",
    language: "Language",
    spread: "Spread",
    destinySpread: "3 Card Destiny",
    dailySpread: "1 Card Daily",
    focus: "Reading focus",
    chooseTopic: "Choose topic",
    askQuestion: "Ask a question",
    topic: "Topic",
    question: "Question",
    questionPlaceholder: "What would you like guidance about?",
    questionRequired: "Enter a question before shuffling the deck.",
    shuffle: "Shuffle Deck",
    shuffleAgain: "Shuffle Again",
    reveal: "Reveal Reading",
    awaitingShuffle: "Awaiting shuffle",
    selected: "selected",
    deckReady: "The deck is ready when you are.",
    deckLabel: "Tarot card deck",
    cardLabel: "Card",
    selectedOrder: "Selection",
    selectingStatus: "Choose the cards that draw your attention.",
    shufflingStatus: "Shuffling the deck…",
    settlingStatus: "Letting the deck settle…",
    dealingStatus: "Dealing the cards into place…",
    resolvingStatus: "Revealing your selected cards…",
    generatingStatus: "Reading the pattern in your cards…",
    deepGeneratingStatus: "Connecting your question with the full spread…",
    revealingStatus: "Your reading is unfolding…",
    readingReady: "Your reading is ready.",
    retryShuffle: "Retry Shuffle",
    retryReveal: "Retry Reveal",
    retryReading: "Retry Reading",
    mainTheme: "Main theme",
    opportunities: "Opportunities",
    challenges: "Challenges",
    guidance: "Guidance",
    shuffleError: "Could not shuffle the deck.",
    revealError: "Could not reveal the selected cards.",
    readingError: "Could not generate the reading.",
    unavailableError: "The reading service is unavailable. Please try again.",
    readingStyle: "Reading style",
    standardReading: "Standard",
    deepReading: "Deep AI",
    premium: "Premium",
    premiumRequired: "Premium access is required for a Deep AI reading."
  },
  th: {
    headline: "เลือก เปิดไพ่ และทบทวน",
    language: "ภาษา",
    spread: "รูปแบบไพ่",
    destinySpread: "ไพ่เส้นทางชีวิต 3 ใบ",
    dailySpread: "ไพ่ประจำวัน 1 ใบ",
    focus: "หัวข้อการอ่านไพ่",
    chooseTopic: "เลือกหัวข้อ",
    askQuestion: "ถามคำถาม",
    topic: "หัวข้อ",
    question: "คำถาม",
    questionPlaceholder: "คุณอยากขอคำแนะนำเรื่องอะไร?",
    questionRequired: "กรุณาใส่คำถามก่อนสับไพ่",
    shuffle: "สับไพ่",
    shuffleAgain: "สับไพ่อีกครั้ง",
    reveal: "เปิดคำทำนาย",
    awaitingShuffle: "รอการสับไพ่",
    selected: "ใบที่เลือก",
    deckReady: "ไพ่พร้อมแล้วเมื่อคุณพร้อม",
    deckLabel: "สำรับไพ่ทาโรต์",
    cardLabel: "ไพ่ใบที่",
    selectedOrder: "ลำดับที่เลือก",
    selectingStatus: "เลือกไพ่ที่ดึงดูดความสนใจของคุณ",
    shufflingStatus: "กำลังสับไพ่…",
    settlingStatus: "กำลังรวบไพ่ให้สงบนิ่ง…",
    dealingStatus: "กำลังแจกไพ่เข้าตำแหน่ง…",
    resolvingStatus: "กำลังเปิดไพ่ที่คุณเลือก…",
    generatingStatus: "กำลังอ่านความสัมพันธ์ของไพ่…",
    deepGeneratingStatus: "กำลังเชื่อมโยงคำถามของคุณกับไพ่ทั้งชุด…",
    revealingStatus: "คำทำนายของคุณกำลังเผยออกมา…",
    readingReady: "คำทำนายของคุณพร้อมแล้ว",
    retryShuffle: "ลองสับไพ่อีกครั้ง",
    retryReveal: "ลองเปิดไพ่อีกครั้ง",
    retryReading: "ลองสร้างคำทำนายอีกครั้ง",
    mainTheme: "ประเด็นหลัก",
    opportunities: "โอกาส",
    challenges: "ความท้าทาย",
    guidance: "คำแนะนำ",
    shuffleError: "ไม่สามารถสับไพ่ได้",
    revealError: "ไม่สามารถเปิดไพ่ที่เลือกได้",
    readingError: "ไม่สามารถสร้างคำทำนายได้",
    unavailableError: "ไม่สามารถเชื่อมต่อบริการอ่านไพ่ได้ กรุณาลองอีกครั้ง",
    readingStyle: "รูปแบบการอ่าน",
    standardReading: "มาตรฐาน",
    deepReading: "วิเคราะห์เชิงลึก",
    premium: "พรีเมียม",
    premiumRequired: "ต้องมีสิทธิ์พรีเมียมสำหรับการอ่านเชิงลึกด้วย AI"
  }
};

const topics: Array<{
  id: TopicId;
  label: Record<Locale, string>;
  request: Record<Locale, string>;
}> = [
    {
      id: "GENERAL",
      label: { en: "General", th: "ภาพรวม" },
      request: { en: "Give me general guidance for this period.", th: "ขอคำแนะนำภาพรวมสำหรับช่วงเวลานี้" }
    },
    {
      id: "LOVE",
      label: { en: "Love", th: "ความรัก" },
      request: { en: "Give me guidance about love and relationships.", th: "ขอคำแนะนำเกี่ยวกับความรักและความสัมพันธ์" }
    },
    {
      id: "CAREER",
      label: { en: "Career", th: "การงาน" },
      request: { en: "Give me guidance about work and my career path.", th: "ขอคำแนะนำเกี่ยวกับงานและเส้นทางอาชีพ" }
    },
    {
      id: "MONEY",
      label: { en: "Money", th: "การเงิน" },
      request: { en: "Give me guidance about money and finances.", th: "ขอคำแนะนำเกี่ยวกับการเงิน" }
    },
    {
      id: "FAMILY",
      label: { en: "Family", th: "ครอบครัว" },
      request: { en: "Give me guidance about family matters.", th: "ขอคำแนะนำเกี่ยวกับครอบครัว" }
    },
    {
      id: "PERSONAL_GROWTH",
      label: { en: "Personal Growth", th: "การพัฒนาตนเอง" },
      request: {
        en: "Give me guidance about personal growth and self-development.",
        th: "ขอคำแนะนำเกี่ยวกับการเติบโตและพัฒนาตนเอง"
      }
    }
  ];

export default function ReadingClient({ initialLocale }: { initialLocale: Locale }) {
  const router = useRouter();
  const locale = initialLocale;
  const [spread, setSpread] = useState<Spread>("DESTINY_3");
  const [questionMode, setQuestionMode] = useState<QuestionMode>("TOPIC");
  const [topic, setTopic] = useState<TopicId>("CAREER");
  const [question, setQuestion] = useState("");
  const [readingMode, setReadingMode] = useState<ReadingMode>("STANDARD");
  const [modelTier, setModelTier] = useState("CORE");
  const [modelTiers, setModelTiers] = useState<ReadingOptions["modelTiers"]>([]);
  const [deepAccess, setDeepAccess] = useState<ReadingOptions["deepReading"]>({
    enabled: false,
    entitled: false,
    upgradeUrl: null,
    authenticated: false,
    premiumExpiresAt: null,
    availableAdEarnedCredits: 0
  });
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
  const readingHeading = useRef<HTMLHeadingElement | null>(null);
  const reduceMotion = useReducedMotion();

  const text = copy[locale];
  const selectLimit = shuffle?.selectCount ?? (spread === "DAILY_1" ? 1 : 3);
  const selectedTopic = topics.find((item) => item.id === topic) ?? topics[0];
  const composedQuestion =
    questionMode === "TOPIC"
      ? `TOPIC: ${selectedTopic.id}\nREQUEST: ${selectedTopic.request[locale]}`
      : question.trim();
  const selectedSet = useMemo(() => new Set(selected), [selected]);
  const selectionOrder = useMemo(() => new Map(selected.map((index, order) => [index, order + 1])), [selected]);
  const isWorking = phase === "SHUFFLING" || phase === "RESOLVING" || phase === "GENERATING";
  const controlsLocked = isWorking || phase === "REVEALING";
  const phaseStatus = getPhaseStatus(phase, readingMode, shuffleVisualStep, text);
  const handleRewardStatus = useCallback((status: RewardedDeepStatus) => {
    setRewardUnlockAvailable(status.enabled);
    setDeepAccess((current) => ({
      ...current,
      entitled: current.premiumExpiresAt !== null || status.availableDeepCredits > 0,
      availableAdEarnedCredits: status.availableDeepCredits,
    }));
  }, []);

  useEffect(() => {
    let active = true;

    fetch(apiUrl("/api/readings/options"), { credentials: "include", cache: "no-store" })
      .then((response) => (response.ok ? readApiData<ReadingOptions>(response) : null))
      .then((options: ReadingOptions | null) => {
        if (active && options?.deepReading) {
          setDeepAccess(options.deepReading);
          setModelTiers(options.modelTiers ?? []);
          const firstAvailable = options.modelTiers?.find((tier) => tier.available);
          if (firstAvailable) setModelTier(firstAvailable.id);
        }
      })
      .catch(() => {
        // Standard readings remain available when capability discovery fails.
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (phase !== "REVEALING" || !reading) return;

    readingHeading.current?.focus({ preventScroll: true });
    const headingBounds = readingHeading.current?.getBoundingClientRect();
    if (headingBounds && (headingBounds.top < 0 || headingBounds.bottom > window.innerHeight)) {
      readingHeading.current?.scrollIntoView({ behavior: reduceMotion ? "auto" : "smooth", block: "start" });
    }

    const version = flowVersion.current;
    const timer = window.setTimeout(() => {
      if (flowVersion.current === version) setPhase("COMPLETE");
    }, reduceMotion ? 0 : 850);

    return () => window.clearTimeout(timer);
  }, [phase, reading, reduceMotion]);

  useEffect(() => () => {
    flowVersion.current += 1;
    activeRequest.current?.abort();
  }, []);

  function cancelActiveFlow() {
    flowVersion.current += 1;
    activeRequest.current?.abort();
    activeRequest.current = null;
  }

  function beginRequest() {
    cancelActiveFlow();
    const controller = new AbortController();
    activeRequest.current = controller;
    return { controller, version: flowVersion.current };
  }

  function isCurrent(version: number) {
    return flowVersion.current === version;
  }

  function resetReadingFlow() {
    cancelActiveFlow();
    setShuffle(null);
    setSelected([]);
    setCards([]);
    setReading(null);
    setPhase("IDLE");
    setShuffleVisualStep("MIXING");
    setFailureStep(null);
    setError(null);
  }

  function changeLocale(nextLocale: Locale) {
    if (nextLocale === locale) return;
    resetReadingFlow();
    router.push(`/${nextLocale}`);
  }

  function changeSpread(nextSpread: Spread) {
    setSpread(nextSpread);
    resetReadingFlow();
  }

  function changeQuestionMode(nextMode: QuestionMode) {
    setQuestionMode(nextMode);
    resetReadingFlow();
  }

  function changeTopic(nextTopic: TopicId) {
    setTopic(nextTopic);
    resetReadingFlow();
  }

  function changeQuestion(nextQuestion: string) {
    setQuestion(nextQuestion);
    resetReadingFlow();
  }

  function changeReadingMode(nextMode: ReadingMode) {
    if (nextMode === "DEEP" && !deepAccess.entitled) {
      if (rewardUnlockAvailable) {
        document.getElementById("rewarded-deep-unlock")?.scrollIntoView({ behavior: "smooth", block: "center" });
        document.getElementById("rewarded-deep-heading")?.focus({ preventScroll: true });
        return;
      }
      if (deepAccess.upgradeUrl) window.location.assign(deepAccess.upgradeUrl);
      else window.location.assign(`/${locale}/login`);
      return;
    }

    setReadingMode(nextMode);
    resetReadingFlow();
  }

  async function startShuffle() {
    if (questionMode === "QUESTION" && !question.trim()) {
      setError(text.questionRequired);
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
      const responsePromise = fetch(apiUrl("/api/deck/shuffle"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ spread }),
        credentials: "include",
        signal: controller.signal
      });
      const [response] = await Promise.all([
        responsePromise,
        waitFor(reduceMotion ? 0 : shuffleTiming.minimumMix, controller.signal)
      ]);

      if (!response.ok) throw new Error(await readApiError(response, text.shuffleError));
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
    } catch (err) {
      if (isAbortError(err) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("SHUFFLING");
      setError(getRequestError(err, text.shuffleError, text.unavailableError));
      setPhase("ERROR");
    }
  }

  function toggleCard(index: number) {
    if (!shuffle || phase !== "SELECTING") return;

    setSelected((current) => {
      if (current.includes(index)) return current.filter((item) => item !== index);
      if (current.length >= selectLimit) return current;
      return [...current, index];
    });
  }

  async function revealAndRead() {
    if (!shuffle || selected.length !== selectLimit) return;
    const currentShuffle = shuffle;
    const { controller, version } = beginRequest();
    setPhase("RESOLVING");
    setFailureStep(null);
    setError(null);

    try {
      const resolved = await fetch(apiUrl(`/api/deck/${currentShuffle.sessionId}/resolve`), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ selectedIndexes: selected }),
        credentials: "include",
        signal: controller.signal
      });

      if (!resolved.ok) throw new Error(await readApiError(resolved, text.revealError));
      const resolvedBody = await readApiData<{ cards: SelectedCard[] }>(resolved);
      if (!isCurrent(version)) return;
      setCards(resolvedBody.cards);
      await generateReading(resolvedBody.cards, currentShuffle, controller, version);
    } catch (err) {
      if (isAbortError(err) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("RESOLVING");
      setError(getRequestError(err, text.revealError, text.unavailableError));
      setPhase("ERROR");
    }
  }

  async function generateReading(
    resolvedCards: SelectedCard[],
    currentShuffle: ShuffleResponse,
    controller: AbortController,
    version: number
  ) {
    setPhase("GENERATING");

    try {
      const generatedPromise = fetch(apiUrl("/api/readings/generate"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          question: composedQuestion,
          spread: currentShuffle.spread,
          locale,
          readingMode,
          modelTier: readingMode === "DEEP" ? modelTier : null,
          cards: resolvedCards
        }),
        credentials: "include",
        signal: controller.signal
      });
      const [generated] = await Promise.all([
        generatedPromise,
        waitFor(reduceMotion ? 0 : 300, controller.signal)
      ]);

      if (!generated.ok) throw new Error(await readApiError(generated, text.readingError));
      const nextReading = await readApiData<ReadingResponse>(generated);
      if (!isCurrent(version)) return;
      activeRequest.current = null;
      setReading(nextReading);
      if (readingMode === "DEEP" && !deepAccess.premiumExpiresAt && deepAccess.availableAdEarnedCredits > 0) {
        const remaining = deepAccess.availableAdEarnedCredits - 1;
        setDeepAccess((current) => ({ ...current, availableAdEarnedCredits: remaining, entitled: remaining > 0 }));
      }
      setFailureStep(null);
      setPhase("REVEALING");
    } catch (err) {
      if (isAbortError(err) || !isCurrent(version)) return;
      activeRequest.current = null;
      setFailureStep("GENERATING");
      setError(getRequestError(err, text.readingError, text.unavailableError));
      setPhase("ERROR");
    }
  }

  async function retryGeneration() {
    if (!shuffle || cards.length === 0) return;
    const { controller, version } = beginRequest();
    setError(null);
    setFailureStep(null);
    await generateReading(cards, shuffle, controller, version);
  }

  function retryFailedStep() {
    if (failureStep === "SHUFFLING") void startShuffle();
    if (failureStep === "RESOLVING") void revealAndRead();
    if (failureStep === "GENERATING") void retryGeneration();
  }

  const retryLabel = failureStep === "SHUFFLING"
    ? text.retryShuffle
    : failureStep === "RESOLVING"
      ? text.retryReveal
      : text.retryReading;

  return (
    <div className="shell">
      <section className="workspace">
        <aside className="control-panel">
          <div>
            <p className="eyebrow">Tarot Destiny</p>
            <h1>{text.headline}</h1>
            {isDev && <a className="admin-link" href="/admin"><Settings aria-hidden="true" size={14} /> Admin</a>}
          </div>

          {readingMode === "DEEP" && modelTiers.length > 0 && (
            <div className="field">
              <label htmlFor="model-tier">Model tier</label>
              <select id="model-tier" value={modelTier} disabled={controlsLocked} onChange={(event) => { setModelTier(event.target.value); resetReadingFlow(); }}>
                {modelTiers.map((tier) => (
                  <option key={tier.id} value={tier.id} disabled={!tier.available}>
                    {formatEnumLabel(tier.id)} · {tier.model}{tier.available ? "" : " (offline)"}
                  </option>
                ))}
              </select>
            </div>
          )}

          <div className="field">
            <label>{text.language}</label>
            <div className="segments" role="group" aria-label={text.language}>
              <button type="button" disabled={controlsLocked} className={locale === "en" ? "active" : ""} aria-pressed={locale === "en"} onClick={() => changeLocale("en")}>
                English
              </button>
              <button type="button" disabled={controlsLocked} className={locale === "th" ? "active" : ""} aria-pressed={locale === "th"} onClick={() => changeLocale("th")}>
                ไทย
              </button>
            </div>
          </div>

          <div className="field">
            <label>{text.spread}</label>
            <div className="segments" role="group" aria-label={text.spread}>
              <button
                type="button"
                className={spread === "DESTINY_3" ? "active" : ""}
                disabled={controlsLocked}
                aria-pressed={spread === "DESTINY_3"}
                onClick={() => changeSpread("DESTINY_3")}
              >
                {text.destinySpread}
              </button>
              <button
                type="button"
                className={spread === "DAILY_1" ? "active" : ""}
                disabled={controlsLocked}
                aria-pressed={spread === "DAILY_1"}
                onClick={() => changeSpread("DAILY_1")}
              >
                {text.dailySpread}
              </button>
            </div>
          </div>

          <RewardedDeepUnlock
            locale={locale}
            premiumEntitled={deepAccess.premiumExpiresAt !== null}
            onStatus={handleRewardStatus}
          />

          <div className="field">
            <label>{text.readingStyle}</label>
            <div className="segments reading-mode" role="group" aria-label={text.readingStyle}>
              <button
                type="button"
                className={readingMode === "STANDARD" ? "active" : ""}
                disabled={controlsLocked}
                aria-pressed={readingMode === "STANDARD"}
                onClick={() => changeReadingMode("STANDARD")}
              >
                <BookOpen aria-hidden="true" size={16} />
                <span>{text.standardReading}</span>
              </button>
              <button
                type="button"
                className={readingMode === "DEEP" ? "active premium-mode" : "premium-mode"}
                aria-pressed={readingMode === "DEEP"}
                onClick={() => changeReadingMode("DEEP")}
                disabled={controlsLocked || !deepAccess.enabled}
                title={!deepAccess.entitled ? text.premiumRequired : undefined}
              >
                <Sparkles aria-hidden="true" size={16} />
                <span>{text.deepReading}</span>
                <small>{text.premium}</small>
              </button>
            </div>
          </div>

          <div className="field">
            <label>{text.focus}</label>
            <div className="segments" role="group" aria-label={text.focus}>
              <button
                type="button"
                className={questionMode === "TOPIC" ? "active" : ""}
                disabled={controlsLocked}
                aria-pressed={questionMode === "TOPIC"}
                onClick={() => changeQuestionMode("TOPIC")}
              >
                {text.chooseTopic}
              </button>
              <button
                type="button"
                className={questionMode === "QUESTION" ? "active" : ""}
                disabled={controlsLocked}
                aria-pressed={questionMode === "QUESTION"}
                onClick={() => changeQuestionMode("QUESTION")}
              >
                {text.askQuestion}
              </button>
            </div>
          </div>

          {questionMode === "TOPIC" ? (
            <div className="field">
              <label htmlFor="reading-topic">{text.topic}</label>
              <select id="reading-topic" value={topic} disabled={controlsLocked} onChange={(event) => changeTopic(event.target.value as TopicId)}>
                {topics.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.label[locale]}
                  </option>
                ))}
              </select>
            </div>
          ) : (
            <div className="field">
              <label htmlFor="reading-question">{text.question}</label>
              <textarea
                id="reading-question"
                value={question}
                onChange={(event) => changeQuestion(event.target.value)}
                placeholder={text.questionPlaceholder}
                rows={4}
                disabled={controlsLocked}
              />
            </div>
          )}

          <button className="primary" type="button" onClick={startShuffle} disabled={controlsLocked}>
            {shuffle ? text.shuffleAgain : text.shuffle}
          </button>

          {shuffle && phase === "SELECTING" && (
            <button className={`secondary reveal-action ${selected.length === selectLimit ? "ready" : ""}`} type="button" onClick={revealAndRead} disabled={selected.length !== selectLimit}>
              {text.reveal}
            </button>
          )}

          {error && (
            <div className="error-panel">
              <p className="error" role="alert">{error}</p>
              {failureStep && (
                <button className="secondary retry-action" type="button" onClick={retryFailedStep}>
                  <RefreshCw aria-hidden="true" size={16} />
                  {retryLabel}
                </button>
              )}
            </div>
          )}
        </aside>

        <section className={`deck-area phase-${phase.toLowerCase()}`} aria-busy={isWorking}>
          <div className="deck-header">
            <div>
              <p className="eyebrow">{shuffle ? `${selected.length}/${selectLimit} ${text.selected}` : text.awaitingShuffle}</p>
              {reading ? (
                <h2 ref={readingHeading} tabIndex={-1}>{reading.title}</h2>
              ) : (
                <h2>{text.deckReady}</h2>
              )}
            </div>
            <span className={`status ${phaseStatus ? "" : "status-empty"}`} role="status" aria-live="polite" aria-atomic="true">
              {phaseStatus}
            </span>
          </div>

          {phase === "SHUFFLING" && (
            shuffleVisualStep === "DEALING" && shuffle
              ? <DealStage cardCount={shuffle.cardCount} reduceMotion={reduceMotion} />
              : <ShuffleStage step={shuffleVisualStep} />
          )}

          {!reading && phase !== "SHUFFLING" && phase !== "RESOLVING" && phase !== "GENERATING" && cards.length === 0 && (
            <div className={`deck-grid ${shuffle ? "is-ready" : "is-idle"}`} aria-label={text.deckLabel}>
              {Array.from({ length: shuffle?.cardCount ?? 78 }, (_, index) => (
                <button
                  key={index}
                  type="button"
                  className={`card-back ${selectedSet.has(index) ? "selected" : ""}`}
                  onClick={() => toggleCard(index)}
                  disabled={!shuffle || phase !== "SELECTING"}
                  aria-pressed={selectedSet.has(index)}
                  aria-label={`${text.cardLabel} ${index + 1}${selectionOrder.has(index) ? `, ${text.selectedOrder} ${selectionOrder.get(index)}` : ""}`}
                  title={`${text.cardLabel} ${index + 1}`}
                >
                  <span className={selectionOrder.has(index) ? "selection-order" : "deck-index"}>
                    {selectionOrder.get(index) ?? index + 1}
                  </span>
                </button>
              ))}
            </div>
          )}

          {!reading && (phase === "RESOLVING" || phase === "GENERATING" || (phase === "ERROR" && cards.length > 0)) && (
            <div className="waiting-stage">
              <WaitingSpread cards={cards} count={selected.length} spread={spread} />
              {isWorking && <ReadingLoader label={phaseStatus} />}
            </div>
          )}

          {reading && (
            <div className={`reading ${phase === "REVEALING" ? "is-revealing" : "is-complete"}`}>
              <div className="revealed-cards">
                {reading.cards.map((card, index) => (
                  <CardReveal card={card} index={index} key={`${card.position}-${card.cardId}`} />
                ))}
              </div>

              <div className="reading-intro reveal-section reveal-section-1">
                <p className="summary">{reading.summary}</p>
                <div className="theme">
                  <span>{text.mainTheme}</span>
                  <strong>{reading.mainTheme}</strong>
                </div>
              </div>

              <div className="insight-grid reveal-section reveal-section-2">
                <Insight title={text.opportunities} items={reading.opportunities} />
                <Insight title={text.challenges} items={reading.challenges} />
                <Insight title={text.guidance} items={reading.guidance} />
              </div>

              <div className="reveal-section reveal-section-3">
                <blockquote>{reading.reflectionQuestion}</blockquote>
                <p className="closing">{reading.closingMessage}</p>
              </div>

              {isDev && (
                <pre className="debug">
                  {JSON.stringify(
                    {
                      cacheStatus: reading.cacheStatus,
                      cacheKey: reading.cacheKey,
                      locale,
                      readingMode: reading.readingMode,
                      generationSource: reading.generationSource,
                      generationModel: reading.generationModel,
                      modelTier: reading.modelTier,
                      inferenceWorker: reading.inferenceWorker,
                      inferenceProvider: reading.inferenceProvider,
                      promptVariant: reading.promptVariant,
                      qualityScore: reading.qualityScore,
                      phase,
                      questionMode,
                      topic: questionMode === "TOPIC" ? topic : null,
                      classification: reading.classification,
                      cards
                    },
                    null,
                    2
                  )}
                </pre>
              )}
            </div>
          )}
        </section>
      </section>
    </div>
  );
}

function ShuffleStage({ step }: { step: ShuffleVisualStep }) {
  return (
    <div className={`shuffle-stage shuffle-${step.toLowerCase()}`} aria-hidden="true">
      <div className="shuffle-aura" />
      <div className="shuffle-stack">
        {Array.from({ length: 7 }, (_, index) => (
          <div className={`shuffle-card shuffle-card-${index + 1}`} key={index} />
        ))}
      </div>
    </div>
  );
}

function DealStage({ cardCount, reduceMotion }: { cardCount: number; reduceMotion: boolean }) {
  const deck = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (reduceMotion || !deck.current) return;

    let animations: Animation[] = [];
    const frame = window.requestAnimationFrame(() => {
      if (!deck.current) return;
      const cards = Array.from(deck.current.querySelectorAll<HTMLElement>(".deal-card"));
      const deckBounds = deck.current.getBoundingClientRect();
      const centerX = deckBounds.left + deckBounds.width / 2;
      const centerY = deckBounds.top + Math.min(deckBounds.height / 2, window.innerHeight * 0.32);
      const dealDuration = 760;
      const availableStagger = Math.max(shuffleTiming.deal - dealDuration - 60, 0);
      const delayStep = cards.length > 1 ? Math.min(15, availableStagger / (cards.length - 1)) : 0;

      animations = cards.map((card, index) => {
        const bounds = card.getBoundingClientRect();
        const offsetX = centerX - (bounds.left + bounds.width / 2);
        const offsetY = centerY - (bounds.top + bounds.height / 2);
        const travelDirection = offsetX >= 0 ? -1 : 1;
        const startingRotation = ((index % 7) - 3) * 0.45;
        const distance = Math.hypot(offsetX, offsetY);
        const arcLift = Math.min(30, 10 + distance * 0.035);
        card.style.zIndex = String(cards.length - index);

        return card.animate(
          [
            {
              opacity: 1,
              transform: `translate3d(${offsetX}px, ${offsetY}px, 0) rotate(${startingRotation}deg) scale(0.94)`
            },
            {
              opacity: 1,
              offset: 0.16,
              transform: `translate3d(${offsetX * 0.96}px, ${offsetY - 12}px, 0) rotate(${startingRotation + travelDirection * 2.6}deg) scale(0.98)`
            },
            {
              opacity: 1,
              offset: 0.68,
              transform: `translate3d(${offsetX * 0.34}px, ${offsetY * 0.32 - arcLift}px, 0) rotate(${travelDirection * 1.4}deg) scale(1.01)`
            },
            {
              opacity: 1,
              transform: "translate3d(0, 0, 0) rotate(0deg) scale(1)"
            }
          ],
          {
            duration: dealDuration,
            delay: index * delayStep,
            easing: "cubic-bezier(0.2, 0.72, 0.22, 1)",
            fill: "both"
          }
        );
      });
    });

    return () => {
      window.cancelAnimationFrame(frame);
      animations.forEach((animation) => animation.cancel());
    };
  }, [cardCount, reduceMotion]);

  return (
    <div className="deal-stage" aria-hidden="true">
      <div className="deal-origin" />
      <div className="deck-grid deal-grid" ref={deck}>
        {Array.from({ length: cardCount }, (_, index) => (
          <div className="card-back deal-card" key={index}>
            <span className="deck-index">{index + 1}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function WaitingSpread({ cards, count, spread }: { cards: SelectedCard[]; count: number; spread: Spread }) {
  const positions = spread === "DAILY_1" ? ["TODAY"] : ["PAST", "PRESENT", "DIRECTION"];
  const visualCards = Array.from({ length: Math.max(count, cards.length) }, (_, index) => ({
    position: cards[index]?.position ?? positions[index] ?? `CARD_${index + 1}`,
    orientation: cards[index]?.orientation
  }));

  return (
    <div className={`waiting-spread waiting-spread-${visualCards.length}`} aria-hidden="true">
      {visualCards.map((card, index) => (
        <div className="waiting-card-wrap" key={`${card.position}-${index}`}>
          <div className={`waiting-card ${card.orientation === "REVERSED" ? "is-reversed" : ""}`} />
          <span>{formatEnumLabel(card.position)}</span>
        </div>
      ))}
    </div>
  );
}

function ReadingLoader({ label }: { label: string }) {
  return (
    <div className="reading-loader" aria-hidden="true">
      <div className="loader-orbit">
        <Sparkles size={22} />
        <i />
        <i />
        <i />
      </div>
      <p>{label}</p>
    </div>
  );
}

function CardReveal({ card, index }: { card: ReadingResponse["cards"][number]; index: number }) {
  return (
    <article className="reading-card" style={{ animationDelay: `${index * 150}ms` }}>
      <div className="card-visual">
        <div className="card-visual-inner" style={{ animationDelay: `${index * 150}ms` }}>
          <div className="card-visual-back" />
          <div className="face card-visual-front">
            <span>{formatEnumLabel(card.position)}</span>
            <Sparkles className={`card-sigil ${card.orientation === "REVERSED" ? "is-reversed" : ""}`} aria-hidden="true" />
            <strong>{card.cardName}</strong>
            <small>{formatEnumLabel(card.orientation)}</small>
          </div>
        </div>
      </div>
      <p>{card.interpretation}</p>
    </article>
  );
}

function Insight({ title, items }: { title: string; items: string[] }) {
  return (
    <section className="insight">
      <h3>{title}</h3>
      {items.map((item) => (
        <p key={item}>{item}</p>
      ))}
    </section>
  );
}

function getPhaseStatus(
  phase: ReadingPhase,
  readingMode: ReadingMode,
  shuffleVisualStep: ShuffleVisualStep,
  text: UiCopy
) {
  switch (phase) {
    case "SHUFFLING":
      if (shuffleVisualStep === "SETTLING") return text.settlingStatus;
      if (shuffleVisualStep === "DEALING") return text.dealingStatus;
      return text.shufflingStatus;
    case "SELECTING":
      return text.selectingStatus;
    case "RESOLVING":
      return text.resolvingStatus;
    case "GENERATING":
      return readingMode === "DEEP" ? text.deepGeneratingStatus : text.generatingStatus;
    case "REVEALING":
      return text.revealingStatus;
    case "COMPLETE":
      return text.readingReady;
    default:
      return "";
  }
}

function useReducedMotion() {
  const [reduceMotion, setReduceMotion] = useState(false);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    const updatePreference = () => setReduceMotion(mediaQuery.matches);
    updatePreference();
    mediaQuery.addEventListener("change", updatePreference);
    return () => mediaQuery.removeEventListener("change", updatePreference);
  }, []);

  return reduceMotion;
}

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

function formatEnumLabel(value: string) {
  return value
    .toLowerCase()
    .split("_")
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

async function readApiError(response: Response, fallback: string) {
  try {
    const payload: unknown = await response.json();
    const messages = collectErrorMessages(payload);
    if (messages.length > 0) return `${fallback} ${messages.join(" ")}`;
  } catch {
    // Some infrastructure errors have an empty or non-JSON response body.
  }

  return `${fallback} (${response.status})`;
}

function collectErrorMessages(payload: unknown): string[] {
  if (typeof payload === "string" && payload.trim()) return [payload.trim()];
  if (Array.isArray(payload)) return payload.flatMap(collectErrorMessages);
  if (!payload || typeof payload !== "object") return [];

  const body = payload as Record<string, unknown>;
  const directMessages = [body.error, body.detail, body.message, body.title].flatMap(collectErrorMessages);
  const validationMessages = collectErrorMessages(body.errors);
  const messages = [...directMessages, ...validationMessages];

  if (messages.length > 0) return [...new Set(messages)];
  return [...new Set(Object.values(body).flatMap(collectErrorMessages))];
}

async function readApiData<T>(response: Response): Promise<T> {
  const payload: unknown = await response.json();

  if (isRecord(payload) && typeof payload.success === "boolean" && "data" in payload) {
    return payload.data as T;
  }

  return payload as T;
}

function apiUrl(path: string) {
  return `${getApiBaseUrl()}${path}`;
}

function getApiBaseUrl() {
  const configuredBase = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  if (configuredBase) return configuredBase.replace(/\/+$/, "");
  return "";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function getRequestError(error: unknown, fallback: string, unavailable: string) {
  if (error instanceof TypeError) return unavailable;
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}
