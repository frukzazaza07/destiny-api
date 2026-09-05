"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { ArrowLeft, Database, Flame, RefreshCw, ShieldCheck, UserCog, UserPlus } from "lucide-react";
import { apiFetch, apiMutation, readApiData, readApiError } from "../../../lib/api-client";
import styles from "./admin.module.css";

type Metrics = { runtime: { llmAvoided: number }; persistent: { answerCount: number; variantCount: number; totalHits: number; mostUsed: HotAnswer[] }; eligibleHitRate: number; baseInterpretationHits: number };
type HotAnswer = { cacheHash: string; domain: string; intent: string; readingMode: string; locale: string; hitCount: number; variantCount: number };
type ReviewExample = { id: string; question: string; locale: string; predictedDomain: string; predictedIntent: string; predictedConfidence: number; predictedPersonalization: "LOW" | "MEDIUM" | "HIGH"; revision: number };
type Warmup = { id: string; status: string; requestedCombinations: number; completedCombinations: number; generatedVariants: number; failedCombinations: number };
type AdminUser = { id: string; email: string; emailVerified: boolean; enabled: boolean; roles: string[]; premiumDeepActive: boolean; premiumExpiresAt: string | null; premiumRevision: number | null; revision: number };
type RewardSettings = { requiredAdCompletions: number; deepCreditsPerCompletedBundle: number; revision: number; updatedAt: string; updatedByUserId: string | null };

export default function AdminPage() {
  const [authorized, setAuthorized] = useState<boolean | null>(null);
  const [metrics, setMetrics] = useState<Metrics | null>(null);
  const [reviews, setReviews] = useState<ReviewExample[]>([]);
  const [warmups, setWarmups] = useState<Warmup[]>([]);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [rewardSettings, setRewardSettings] = useState<RewardSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [warmMode, setWarmMode] = useState("STANDARD");
  const [warmVariants, setWarmVariants] = useState(1);
  const [warmCount, setWarmCount] = useState(100);
  const [grantDays, setGrantDays] = useState(30);

  const load = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      const responses = await Promise.all([
        apiFetch("/api/admin/cache/analytics", { cache: "no-store" }),
        apiFetch("/api/classifier/training-examples?status=PENDING&pageSize=50", { cache: "no-store" }),
        apiFetch("/api/admin/cache/warmups", { cache: "no-store" }),
        apiFetch("/api/admin/users?pageSize=50", { cache: "no-store" }),
        apiFetch("/api/admin/rewarded-deep/settings", { cache: "no-store" })
      ]);
      if (responses.some((response) => !response.ok)) throw new Error("Administrator data could not be loaded.");
      const [analytics, reviewPage, jobs, userPage, rewarded] = await Promise.all([
        readApiData<Metrics>(responses[0]), readApiData<{ items: ReviewExample[] }>(responses[1]),
        readApiData<Warmup[]>(responses[2]), readApiData<{ items: AdminUser[] }>(responses[3]),
        readApiData<RewardSettings>(responses[4])
      ]);
      setMetrics(analytics); setReviews(reviewPage.items); setWarmups(jobs); setUsers(userPage.items); setRewardSettings(rewarded);
    } catch (reason) { setError(reason instanceof Error ? reason.message : "Could not load administrator data."); }
    finally { setBusy(false); }
  }, []);

  useEffect(() => {
    apiFetch("/api/auth/me", { cache: "no-store" })
      .then((response) => readApiData<{ authenticated: boolean; roles: string[] }>(response))
      .then((account) => { const allowed = account.authenticated && account.roles.includes("ADMIN"); setAuthorized(allowed); if (allowed) void load(); })
      .catch(() => setAuthorized(false));
  }, [load]);

  useEffect(() => {
    if (!authorized || !warmups.some((job) => job.status === "QUEUED" || job.status === "RUNNING")) return;
    const timer = window.setInterval(() => void load(), 4000);
    return () => window.clearInterval(timer);
  }, [authorized, load, warmups]);

  async function mutate(path: string, method: string, body?: unknown) {
    setError(null);
    const response = await apiMutation(path, method, body);
    if (!response.ok) throw new Error(await readApiError(response, "The update failed."));
    await load();
  }

  async function createUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const form = event.currentTarget; const values = new FormData(form);
    try { await mutate("/api/admin/users", "POST", { email: values.get("email"), password: values.get("password"), emailVerified: true, isAdmin: values.get("isAdmin") === "on" }); form.reset(); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "User creation failed."); }
  }

  async function review(example: ReviewExample, status: "APPROVED" | "REJECTED") {
    try { await mutate(`/api/classifier/training-examples/${example.id}/review`, "PUT", { status, domain: status === "APPROVED" ? example.predictedDomain : null, intent: status === "APPROVED" ? example.predictedIntent : null, personalization: status === "APPROVED" ? example.predictedPersonalization : null, paraphraseGroup: status === "APPROVED" ? `production-${example.id}` : null, expectedRevision: example.revision }); }
    catch (reason) { setError(reason instanceof Error ? reason.message : "Review failed."); }
  }

  if (authorized === null) return <main className={styles.shell}><p aria-live="polite">Checking administrator session…</p></main>;
  if (!authorized) return <main className={styles.shell}><section className={styles.panel}><h1>Administrator sign-in required</h1><p>The legacy administrator key is no longer stored in browser JavaScript.</p><a href="/en/login?return=%2Fadmin">Log in with an administrator account</a></section></main>;

  return (
    <main className={styles.shell}>
      <header className={styles.header}><div><p>Tarot Destiny · Operations</p><h1>Accounts, cache & classifier</h1></div><a href="/en"><ArrowLeft size={16}/>Reading app</a></header>
      <section className={styles.keybar}><ShieldCheck size={18}/><strong>Role-authenticated administrator session</strong><button type="button" className={styles.secondary} onClick={() => void load()} disabled={busy}><RefreshCw size={15}/>Refresh</button></section>
      {error && <p className={styles.error} role="alert">{error}</p>}

      <section className={styles.panel}>
        <div className={styles.panelTitle}><UserCog size={18}/><div><h2>Users & Premium DEEP</h2><p>Premium changes invalidate active sessions immediately.</p></div></div>
        <form className={styles.userForm} onSubmit={createUser}><UserPlus size={17}/><input name="email" type="email" required placeholder="user@example.com" aria-label="New user email"/><input name="password" type="password" minLength={12} required placeholder="Temporary strong password" aria-label="Temporary password"/><label><input name="isAdmin" type="checkbox"/>Administrator</label><button type="submit">Create user</button></form>
        <div className={styles.premiumControls}><label>Premium extension days<input type="number" min={1} max={3650} value={grantDays} onChange={(event) => setGrantDays(Number(event.target.value))}/></label></div>
        <div className={styles.tableWrap}><table><thead><tr><th>Account</th><th>Status</th><th>Premium</th><th>Actions</th></tr></thead><tbody>{users.map((user) => <tr key={user.id}><td><strong>{user.email}</strong><small>{user.roles.join(", ")} · {user.emailVerified ? "verified" : "unverified"}</small></td><td>{user.enabled ? "Enabled" : "Disabled"}</td><td>{user.premiumDeepActive ? `Until ${new Date(user.premiumExpiresAt!).toLocaleString()}` : "None"}</td><td><div className={styles.rowActions}><button type="button" onClick={() => void mutate(`/api/admin/users/${user.id}/premium`, "POST", { days: grantDays, expectedRevision: user.premiumRevision })}>{user.premiumDeepActive ? "Extend" : "Grant"}</button>{user.premiumDeepActive && <button type="button" className={styles.reject} onClick={() => void mutate(`/api/admin/users/${user.id}/premium`, "DELETE", { expectedRevision: user.premiumRevision })}>Revoke</button>}<button type="button" className={styles.secondary} onClick={() => void mutate(`/api/admin/users/${user.id}/status`, "PUT", { enabled: !user.enabled, expectedRevision: user.revision })}>{user.enabled ? "Disable" : "Enable"}</button></div></td></tr>)}</tbody></table></div>
      </section>

      {rewardSettings && <section className={styles.panel}>
        <div className={styles.panelTitle}><ShieldCheck size={18}/><div><h2>Rewarded DEEP settings</h2><p>Database values are snapshotted onto each new session. The rollout flag separately controls serving.</p></div></div>
        <form className={styles.rewardForm} onSubmit={(event) => { event.preventDefault(); const values = new FormData(event.currentTarget); void mutate("/api/admin/rewarded-deep/settings", "PUT", { requiredAdCompletions: Number(values.get("required")), deepCreditsPerCompletedBundle: Number(values.get("credits")), expectedRevision: rewardSettings.revision }).catch((reason) => setError(reason instanceof Error ? reason.message : "Reward settings update failed.")); }}>
          <label>Required completed ads<input name="required" type="number" min={1} max={10} defaultValue={rewardSettings.requiredAdCompletions} required/></label>
          <label>DEEP credits per bundle<input name="credits" type="number" min={1} max={5} defaultValue={rewardSettings.deepCreditsPerCompletedBundle} required/></label>
          <button type="submit">Save reward settings</button>
          <small>Revision {rewardSettings.revision} · Updated {new Date(rewardSettings.updatedAt).toLocaleString()}</small>
        </form>
      </section>}

      {metrics && <section className={styles.metrics} aria-label="Cache metrics"><Metric label="Eligible hit rate" value={`${(metrics.eligibleHitRate * 100).toFixed(1)}%`} accent/><Metric label="Persistent answers" value={format(metrics.persistent.answerCount)}/><Metric label="Answer variants" value={format(metrics.persistent.variantCount)}/><Metric label="Persistent reuses" value={format(metrics.persistent.totalHits)}/><Metric label="LLM calls avoided" value={format(metrics.runtime.llmAvoided)}/><Metric label="Base cache hits" value={format(metrics.baseInterpretationHits)}/></section>}

      <div className={styles.columns}>
        <section className={styles.panel}><div className={styles.panelTitle}><Database size={18}/><div><h2>Most-used readings</h2><p>Hashed identities only; no raw questions.</p></div></div><div className={styles.tableWrap}><table><thead><tr><th>Intent</th><th>Mode</th><th>Locale</th><th>Variants</th><th>Hits</th></tr></thead><tbody>{metrics?.persistent.mostUsed.map((item) => <tr key={item.cacheHash}><td><strong>{item.intent}</strong><small>{item.domain}</small></td><td>{item.readingMode}</td><td>{item.locale}</td><td>{item.variantCount}</td><td>{item.hitCount}</td></tr>)}</tbody></table></div></section>
        <section className={styles.panel}><div className={styles.panelTitle}><Flame size={18}/><div><h2>One-card warming</h2><p>Process a bounded offline batch.</p></div></div><div className={styles.formRow}><select value={warmMode} onChange={(event) => setWarmMode(event.target.value)}><option>STANDARD</option><option>DEEP</option></select><label>Variants<input type="number" min={1} max={10} value={warmVariants} onChange={(event) => setWarmVariants(Number(event.target.value))}/></label><label>Batch<input type="number" min={1} max={10000} value={warmCount} onChange={(event) => setWarmCount(Number(event.target.value))}/></label><button type="button" onClick={() => void mutate("/api/admin/cache/warmups", "POST", { readingMode: warmMode, variants: warmVariants, maxCombinations: warmCount, offset: 0 })}>Start</button></div><div className={styles.jobs}>{warmups.map((job) => <article key={job.id}><span className={styles[job.status.toLowerCase()] ?? ""}>{job.status}</span><strong>{job.completedCombinations}/{job.requestedCombinations} combinations</strong><small>{job.generatedVariants} variants · {job.failedCombinations} failed</small></article>)}</div></section>
      </div>

      <section className={styles.panel}><div className={styles.panelTitle}><ShieldCheck size={18}/><div><h2>Classifier label review</h2><p>{reviews.length} consented examples awaiting review.</p></div></div><div className={styles.reviewGrid}>{reviews.map((example) => <article key={example.id} className={styles.reviewCard}><div><span>{example.locale.toUpperCase()}</span><span>{Math.round(example.predictedConfidence * 100)}% confidence</span></div><blockquote>{example.question}</blockquote><p>{example.predictedDomain} / <strong>{example.predictedIntent}</strong></p><footer><button type="button" onClick={() => void review(example, "APPROVED")}>Approve label</button><button type="button" className={styles.reject} onClick={() => void review(example, "REJECTED")}>Reject</button></footer></article>)}{reviews.length === 0 && <p className={styles.empty}>No pending examples.</p>}</div></section>
    </main>
  );
}

function Metric({ label, value, accent = false }: { label: string; value: string; accent?: boolean }) { return <article className={accent ? styles.metricAccent : ""}><span>{label}</span><strong>{value}</strong></article>; }
function format(value: number) { return new Intl.NumberFormat("en-US").format(value); }
