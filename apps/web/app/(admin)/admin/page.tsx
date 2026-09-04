"use client";

import { useCallback, useEffect, useState } from "react";
import { ArrowLeft, Database, Flame, KeyRound, RefreshCw, ShieldCheck } from "lucide-react";
import styles from "./admin.module.css";

type Metrics = {
  runtime: { readingRequests: number; cacheHits: number; cacheMisses: number; cacheSkipped: number; llmCalls: number; llmAvoided: number; llmFailures: number; averageLlmLatencyMs: number; promptVariants: Array<{ experimentId: string | null; variantId: string; successes: number; failures: number; averageQualityScore: number }> };
  persistent: { answerCount: number; variantCount: number; totalHits: number; mostUsed: HotAnswer[] };
  eligibleHitRate: number;
  baseInterpretationHits: number;
  baseInterpretationMisses: number;
};
type HotAnswer = { cacheHash: string; domain: string; intent: string; readingMode: string; locale: string; hitCount: number; variantCount: number };
type ReviewExample = { id: string; question: string; locale: string; predictedDomain: string; predictedIntent: string; predictedConfidence: number; predictedPersonalization: "LOW" | "MEDIUM" | "HIGH"; reviewStatus: string; revision: number };
type ReviewPage = { items: ReviewExample[]; total: number };
type Warmup = { id: string; status: string; offset: number; requestedCombinations: number; totalCombinations: number; completedCombinations: number; generatedVariants: number; failedCombinations: number; lastError: string | null };

export default function AdminPage() {
  const [adminKey, setAdminKey] = useState("");
  const [metrics, setMetrics] = useState<Metrics | null>(null);
  const [reviews, setReviews] = useState<ReviewExample[]>([]);
  const [warmups, setWarmups] = useState<Warmup[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [warmMode, setWarmMode] = useState("STANDARD");
  const [warmVariants, setWarmVariants] = useState(1);
  const [warmCount, setWarmCount] = useState(100);

  const load = useCallback(async (key: string) => {
    if (!key) return;
    setBusy(true);
    setError(null);
    try {
      const headers = { "X-Admin-Key": key };
      const [analyticsResponse, reviewsResponse, warmupsResponse] = await Promise.all([
        fetch(apiUrl("/api/admin/cache/analytics"), { headers }),
        fetch(apiUrl("/api/classifier/training-examples?status=PENDING&pageSize=50"), { headers }),
        fetch(apiUrl("/api/admin/cache/warmups"), { headers })
      ]);
      if (!analyticsResponse.ok || !reviewsResponse.ok || !warmupsResponse.ok) throw new Error("Admin access failed. Check the API and admin key.");
      const [analytics, reviewPage, jobs] = await Promise.all([
        readData<Metrics>(analyticsResponse), readData<ReviewPage>(reviewsResponse), readData<Warmup[]>(warmupsResponse)
      ]);
      setMetrics(analytics);
      setReviews(reviewPage.items);
      setWarmups(jobs);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not load admin data.");
    } finally {
      setBusy(false);
    }
  }, []);

  useEffect(() => {
    const saved = window.sessionStorage.getItem("tarot-admin-key") ?? "";
    setAdminKey(saved);
    void load(saved);
  }, [load]);

  useEffect(() => {
    if (!adminKey || !warmups.some((job) => job.status === "QUEUED" || job.status === "RUNNING")) return;
    const timer = window.setInterval(() => void load(adminKey), 4000);
    return () => window.clearInterval(timer);
  }, [adminKey, load, warmups]);

  function connect() {
    window.sessionStorage.setItem("tarot-admin-key", adminKey);
    void load(adminKey);
  }

  async function review(example: ReviewExample, status: "APPROVED" | "REJECTED") {
    setError(null);
    const response = await fetch(apiUrl(`/api/classifier/training-examples/${example.id}/review`), {
      method: "PUT",
      headers: { "Content-Type": "application/json", "X-Admin-Key": adminKey },
      body: JSON.stringify({
        status,
        domain: status === "APPROVED" ? example.predictedDomain : null,
        intent: status === "APPROVED" ? example.predictedIntent : null,
        personalization: status === "APPROVED" ? example.predictedPersonalization : null,
        paraphraseGroup: status === "APPROVED" ? `production-${example.id}` : null,
        expectedRevision: example.revision
      })
    });
    if (!response.ok) {
      setError("Review failed. Reload the queue and try again.");
      return;
    }
    setReviews((items) => items.filter((item) => item.id !== example.id));
  }

  async function startWarmup() {
    const response = await fetch(apiUrl("/api/admin/cache/warmups"), {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Admin-Key": adminKey },
      body: JSON.stringify({ readingMode: warmMode, variants: warmVariants, maxCombinations: warmCount, offset: 0 })
    });
    if (!response.ok) {
      setError("Could not enqueue the warmup job.");
      return;
    }
    await load(adminKey);
  }

  return (
    <main className={styles.shell}>
      <header className={styles.header}>
        <div>
          <p>Tarot Destiny · Operations</p>
          <h1>Cache & classifier control room</h1>
        </div>
        <a href="/th"><ArrowLeft size={16} /> Reading app</a>
      </header>

      <section className={styles.keybar}>
        <KeyRound aria-hidden="true" size={18} />
        <input type="password" value={adminKey} onChange={(event) => setAdminKey(event.target.value)} placeholder="X-Admin-Key" aria-label="Admin key" />
        <button type="button" onClick={connect}>Connect</button>
        <button type="button" className={styles.secondary} onClick={() => void load(adminKey)} disabled={!adminKey || busy}><RefreshCw size={15} /> Refresh</button>
      </section>

      {error && <p className={styles.error} role="alert">{error}</p>}

      {metrics && (
        <section className={styles.metrics} aria-label="Cache metrics">
          <Metric label="Eligible hit rate" value={`${(metrics.eligibleHitRate * 100).toFixed(1)}%`} accent />
          <Metric label="Persistent answers" value={format(metrics.persistent.answerCount)} />
          <Metric label="Answer variants" value={format(metrics.persistent.variantCount)} />
          <Metric label="Persistent reuses" value={format(metrics.persistent.totalHits)} />
          <Metric label="LLM calls avoided" value={format(metrics.runtime.llmAvoided)} />
          <Metric label="Base cache hits" value={format(metrics.baseInterpretationHits)} />
        </section>
      )}

      <div className={styles.columns}>
        <section className={styles.panel}>
          <div className={styles.panelTitle}><Database size={18} /><div><h2>Most-used readings</h2><p>Hashed identities only; no raw questions.</p></div></div>
          <div className={styles.tableWrap}>
            <table><thead><tr><th>Intent</th><th>Mode</th><th>Locale</th><th>Variants</th><th>Hits</th></tr></thead>
              <tbody>{metrics?.persistent.mostUsed.map((item) => <tr key={item.cacheHash}><td><strong>{item.intent}</strong><small>{item.domain}</small></td><td>{item.readingMode}</td><td>{item.locale}</td><td>{item.variantCount}</td><td>{item.hitCount}</td></tr>)}</tbody>
            </table>
          </div>
        </section>

        <section className={styles.panel}>
          <div className={styles.panelTitle}><Flame size={18} /><div><h2>One-card warming</h2><p>Process a bounded offline batch.</p></div></div>
          <div className={styles.formRow}>
            <select value={warmMode} onChange={(event) => setWarmMode(event.target.value)}><option>STANDARD</option><option>DEEP</option></select>
            <label>Variants<input type="number" min={1} max={10} value={warmVariants} onChange={(event) => setWarmVariants(Number(event.target.value))} /></label>
            <label>Batch<input type="number" min={1} max={10000} value={warmCount} onChange={(event) => setWarmCount(Number(event.target.value))} /></label>
            <button type="button" onClick={() => void startWarmup()} disabled={!adminKey}>Start</button>
          </div>
          <div className={styles.jobs}>{warmups.map((job) => <article key={job.id}><span className={styles[job.status.toLowerCase()] ?? ""}>{job.status}</span><strong>{job.completedCombinations}/{job.requestedCombinations} combinations</strong><small>{job.generatedVariants} variants · {job.failedCombinations} failed</small></article>)}</div>
        </section>
      </div>

      {metrics && metrics.runtime.promptVariants.length > 0 && (
        <section className={styles.panel}>
          <div className={styles.panelTitle}><Flame size={18} /><div><h2>Prompt experiment outcomes</h2><p>Automated assignment, success, failure, and quality signals.</p></div></div>
          <div className={styles.tableWrap}><table><thead><tr><th>Experiment</th><th>Variant</th><th>Successes</th><th>Failures</th><th>Avg quality</th></tr></thead><tbody>
            {metrics.runtime.promptVariants.map((variant) => <tr key={`${variant.experimentId}-${variant.variantId}`}><td>{variant.experimentId ?? "None"}</td><td>{variant.variantId}</td><td>{variant.successes}</td><td>{variant.failures}</td><td>{(variant.averageQualityScore * 100).toFixed(1)}%</td></tr>)}
          </tbody></table></div>
        </section>
      )}

      <section className={styles.panel}>
        <div className={styles.panelTitle}><ShieldCheck size={18} /><div><h2>Classifier label review</h2><p>{reviews.length} consented examples awaiting review.</p></div></div>
        <div className={styles.reviewGrid}>
          {reviews.map((example) => <article key={example.id} className={styles.reviewCard}>
            <div><span>{example.locale.toUpperCase()}</span><span>{Math.round(example.predictedConfidence * 100)}% confidence</span></div>
            <blockquote>{example.question}</blockquote>
            <p>{example.predictedDomain} / <strong>{example.predictedIntent}</strong></p>
            <footer><button type="button" onClick={() => void review(example, "APPROVED")}>Approve label</button><button type="button" className={styles.reject} onClick={() => void review(example, "REJECTED")}>Reject</button></footer>
          </article>)}
          {reviews.length === 0 && <p className={styles.empty}>No pending examples.</p>}
        </div>
      </section>
    </main>
  );
}

function Metric({ label, value, accent = false }: { label: string; value: string; accent?: boolean }) {
  return <article className={accent ? styles.metricAccent : ""}><span>{label}</span><strong>{value}</strong></article>;
}
function format(value: number) { return new Intl.NumberFormat("en-US").format(value); }
async function readData<T>(response: Response): Promise<T> { const body = await response.json(); return (body?.data ?? body) as T; }
function apiUrl(path: string) {
  const configured = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();
  if (configured) return `${configured.replace(/\/+$/, "")}${path}`;
  return path;
}
