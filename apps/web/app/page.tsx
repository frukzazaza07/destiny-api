"use client";

import { useEffect, useMemo, useState } from "react";
import { BookOpen, Settings, Sparkles } from "lucide-react";

type Locale = "en" | "th";
type QuestionMode = "TOPIC" | "QUESTION";
type Spread = "DESTINY_3" | "DAILY_1";
type Orientation = "UPRIGHT" | "REVERSED";
type CacheStatus = "HIT" | "MISS" | "SKIPPED";
type ReadingMode = "STANDARD" | "DEEP";
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
  working: string;
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
    working: "Working",
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
    working: "กำลังดำเนินการ",
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

export default function Home() {
  const [locale, setLocale] = useState<Locale>("en");
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
    upgradeUrl: null
  });
  const [shuffle, setShuffle] = useState<ShuffleResponse | null>(null);
  const [selected, setSelected] = useState<number[]>([]);
  const [cards, setCards] = useState<SelectedCard[]>([]);
  const [reading, setReading] = useState<ReadingResponse | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const text = copy[locale];
  const selectLimit = shuffle?.selectCount ?? (spread === "DAILY_1" ? 1 : 3);
  const selectedTopic = topics.find((item) => item.id === topic) ?? topics[0];
  const composedQuestion =
    questionMode === "TOPIC"
      ? `TOPIC: ${selectedTopic.id}\nREQUEST: ${selectedTopic.request[locale]}`
      : question.trim();
  const selectedSet = useMemo(() => new Set(selected), [selected]);

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  useEffect(() => {
    let active = true;

    fetch(apiUrl("/api/readings/options"))
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

  function resetReadingFlow() {
    setShuffle(null);
    setSelected([]);
    setCards([]);
    setReading(null);
    setError(null);
  }

  function changeLocale(nextLocale: Locale) {
    setLocale(nextLocale);
    resetReadingFlow();
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
      if (deepAccess.upgradeUrl) window.location.assign(deepAccess.upgradeUrl);
      return;
    }

    setReadingMode(nextMode);
    resetReadingFlow();
  }

  async function startShuffle() {
    if (questionMode === "QUESTION" && !question.trim()) {
      setError(text.questionRequired);
      return;
    }

    setBusy(true);
    setError(null);
    setReading(null);
    setCards([]);
    setSelected([]);

    try {
      const response = await fetch(apiUrl("/api/deck/shuffle"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ spread })
      });

      if (!response.ok) throw new Error(await readApiError(response, text.shuffleError));
      setShuffle(await readApiData<ShuffleResponse>(response));
    } catch (err) {
      setError(getRequestError(err, text.shuffleError, text.unavailableError));
    } finally {
      setBusy(false);
    }
  }

  function toggleCard(index: number) {
    if (!shuffle || cards.length > 0) return;

    setSelected((current) => {
      if (current.includes(index)) return current.filter((item) => item !== index);
      if (current.length >= selectLimit) return current;
      return [...current, index];
    });
  }

  async function revealAndRead() {
    if (!shuffle || selected.length !== selectLimit) return;
    setBusy(true);
    setError(null);

    try {
      const resolved = await fetch(apiUrl(`/api/deck/${shuffle.sessionId}/resolve`), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ selectedIndexes: selected })
      });

      if (!resolved.ok) throw new Error(await readApiError(resolved, text.revealError));
      const resolvedBody = await readApiData<{ cards: SelectedCard[] }>(resolved);
      setCards(resolvedBody.cards);

      const generated = await fetch(apiUrl("/api/readings/generate"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          question: composedQuestion,
          spread: shuffle.spread,
          locale,
          readingMode,
          modelTier: readingMode === "DEEP" ? modelTier : null,
          cards: resolvedBody.cards
        })
      });

      if (!generated.ok) throw new Error(await readApiError(generated, text.readingError));
      setReading(await readApiData<ReadingResponse>(generated));
    } catch (err) {
      setError(getRequestError(err, text.readingError, text.unavailableError));
    } finally {
      setBusy(false);
    }
  }

  return (
    <main className="shell">
      <section className="workspace">
        <aside className="control-panel">
          <div>
            <p className="eyebrow">Tarot Destiny</p>
            <h1>{text.headline}</h1>
            <a className="admin-link" href="/admin"><Settings aria-hidden="true" size={14} /> Admin</a>
          </div>

          {readingMode === "DEEP" && modelTiers.length > 0 && (
            <div className="field">
              <label htmlFor="model-tier">Model tier</label>
              <select id="model-tier" value={modelTier} onChange={(event) => { setModelTier(event.target.value); resetReadingFlow(); }}>
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
              <button type="button" className={locale === "en" ? "active" : ""} aria-pressed={locale === "en"} onClick={() => changeLocale("en")}>
                English
              </button>
              <button type="button" className={locale === "th" ? "active" : ""} aria-pressed={locale === "th"} onClick={() => changeLocale("th")}>
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
                aria-pressed={spread === "DESTINY_3"}
                onClick={() => changeSpread("DESTINY_3")}
              >
                {text.destinySpread}
              </button>
              <button
                type="button"
                className={spread === "DAILY_1" ? "active" : ""}
                aria-pressed={spread === "DAILY_1"}
                onClick={() => changeSpread("DAILY_1")}
              >
                {text.dailySpread}
              </button>
            </div>
          </div>

          <div className="field">
            <label>{text.readingStyle}</label>
            <div className="segments reading-mode" role="group" aria-label={text.readingStyle}>
              <button
                type="button"
                className={readingMode === "STANDARD" ? "active" : ""}
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
                disabled={!deepAccess.enabled || (!deepAccess.entitled && !deepAccess.upgradeUrl)}
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
                aria-pressed={questionMode === "TOPIC"}
                onClick={() => changeQuestionMode("TOPIC")}
              >
                {text.chooseTopic}
              </button>
              <button
                type="button"
                className={questionMode === "QUESTION" ? "active" : ""}
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
              <select id="reading-topic" value={topic} onChange={(event) => changeTopic(event.target.value as TopicId)}>
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
              />
            </div>
          )}

          <button className="primary" type="button" onClick={startShuffle} disabled={busy}>
            {shuffle ? text.shuffleAgain : text.shuffle}
          </button>

          {shuffle && (
            <button className="secondary" type="button" onClick={revealAndRead} disabled={busy || selected.length !== selectLimit}>
              {text.reveal}
            </button>
          )}

          {error && (
            <p className="error" role="alert">
              {error}
            </p>
          )}
        </aside>

        <section className="deck-area">
          <div className="deck-header">
            <div>
              <p className="eyebrow">{shuffle ? `${selected.length}/${selectLimit} ${text.selected}` : text.awaitingShuffle}</p>
              <h2>{reading ? reading.title : text.deckReady}</h2>
            </div>
            {busy && <span className="status">{text.working}</span>}
          </div>

          {!reading && (
            <div className="deck-grid" aria-label="Tarot card deck">
              {Array.from({ length: shuffle?.cardCount ?? 78 }, (_, index) => (
                <button
                  key={index}
                  type="button"
                  className={`card-back ${selectedSet.has(index) ? "selected" : ""}`}
                  onClick={() => toggleCard(index)}
                  disabled={!shuffle || busy}
                  aria-pressed={selectedSet.has(index)}
                  title={`Card ${index + 1}`}
                >
                  <span>{index + 1}</span>
                </button>
              ))}
            </div>
          )}

          {reading && (
            <div className="reading">
              <p className="summary">{reading.summary}</p>
              <div className="theme">
                <span>{text.mainTheme}</span>
                <strong>{reading.mainTheme}</strong>
              </div>

              <div className="revealed-cards">
                {reading.cards.map((card) => (
                  <article className="reading-card" key={`${card.position}-${card.cardId}`}>
                    <div className="face">
                      <span>{formatEnumLabel(card.position)}</span>
                      <strong>{card.cardName}</strong>
                      <small>{formatEnumLabel(card.orientation)}</small>
                    </div>
                    <p>{card.interpretation}</p>
                  </article>
                ))}
              </div>

              <div className="insight-grid">
                <Insight title={text.opportunities} items={reading.opportunities} />
                <Insight title={text.challenges} items={reading.challenges} />
                <Insight title={text.guidance} items={reading.guidance} />
              </div>

              <blockquote>{reading.reflectionQuestion}</blockquote>
              <p className="closing">{reading.closingMessage}</p>

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
    </main>
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

  if (typeof window !== "undefined") {
    return `${window.location.protocol}//${window.location.hostname}:5000`;
  }

  return "http://localhost:5000";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function getRequestError(error: unknown, fallback: string, unavailable: string) {
  if (error instanceof TypeError) return unavailable;
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}
