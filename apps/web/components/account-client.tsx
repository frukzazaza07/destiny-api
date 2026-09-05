"use client";

import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { apiFetch, apiMutation, readApiData, readApiError } from "../lib/api-client";
import { localizedPath, type Locale } from "../lib/i18n";

type Mode = "login" | "register" | "account" | "verify" | "reset";
type Account = {
  authenticated: boolean;
  email: string | null;
  emailVerified: boolean;
  roles: string[];
  premiumDeepActive: boolean;
  premiumExpiresAt: string | null;
  registrationEnabled: boolean;
  serviceAvailable: boolean;
};

const words = {
  en: {
    login: "Log in", register: "Create account", account: "Your account", email: "Email", password: "Password",
    confirm: "Confirm password", submit: "Continue", unavailable: "Account service is not available.", invalid: "Please check the form and try again.",
    noAccount: "Need an account?", hasAccount: "Already have an account?", reset: "Reset password", verification: "Verify email",
    sendReset: "Send reset link", sendVerify: "Send verification link", token: "Single-use token", newPassword: "New password",
    signedOut: "You are not signed in.", standard: "STANDARD readings remain available without an account.", verified: "Email verified",
    unverified: "Email verification pending", premium: "Premium DEEP access", active: "Active", inactive: "Not active", expires: "Expires",
    admin: "Open administrator console", deleteTitle: "Delete account", delete: "Disable and sign out", deleteHelp: "Enter your password to disable this account and revoke every session.",
    success: "Request completed.", genericMail: "If the account is eligible, an email will be sent.", registrationClosed: "Public registration is currently closed. An administrator can create an account for you."
  },
  th: {
    login: "เข้าสู่ระบบ", register: "สร้างบัญชี", account: "บัญชีของคุณ", email: "อีเมล", password: "รหัสผ่าน",
    confirm: "ยืนยันรหัสผ่าน", submit: "ดำเนินการต่อ", unavailable: "ระบบบัญชียังไม่พร้อมใช้งาน", invalid: "โปรดตรวจสอบข้อมูลแล้วลองอีกครั้ง",
    noAccount: "ยังไม่มีบัญชี?", hasAccount: "มีบัญชีแล้ว?", reset: "รีเซ็ตรหัสผ่าน", verification: "ยืนยันอีเมล",
    sendReset: "ส่งลิงก์รีเซ็ต", sendVerify: "ส่งลิงก์ยืนยัน", token: "โทเค็นใช้ครั้งเดียว", newPassword: "รหัสผ่านใหม่",
    signedOut: "คุณยังไม่ได้เข้าสู่ระบบ", standard: "คุณยังใช้การอ่านแบบ STANDARD ได้โดยไม่ต้องมีบัญชี", verified: "ยืนยันอีเมลแล้ว",
    unverified: "รอการยืนยันอีเมล", premium: "สิทธิ์ Premium DEEP", active: "ใช้งานอยู่", inactive: "ยังไม่มีสิทธิ์", expires: "หมดอายุ",
    admin: "เปิดหน้าผู้ดูแลระบบ", deleteTitle: "ลบบัญชี", delete: "ปิดบัญชีและออกจากระบบ", deleteHelp: "กรอกรหัสผ่านเพื่อปิดบัญชีและยกเลิกทุกเซสชัน",
    success: "ดำเนินการสำเร็จ", genericMail: "หากบัญชีเข้าเงื่อนไข ระบบจะส่งอีเมลให้", registrationClosed: "ขณะนี้ยังไม่เปิดรับสมัครสาธารณะ ผู้ดูแลระบบสามารถสร้างบัญชีให้คุณได้"
  }
} as const;

export default function AccountClient({ locale, mode }: { locale: Locale; mode: Mode }) {
  const text = words[locale];
  const router = useRouter();
  const [account, setAccount] = useState<Account | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [email, setEmail] = useState("");
  const [token, setToken] = useState("");

  useEffect(() => {
    apiFetch("/api/auth/me", { cache: "no-store" })
      .then((response) => readApiData<Account>(response))
      .then(setAccount)
      .catch(() => setError(text.unavailable));
  }, [text.unavailable]);

  useEffect(() => {
    const fragment = new URLSearchParams(window.location.hash.replace(/^#/, ""));
    setEmail(fragment.get("email") ?? "");
    setToken(fragment.get("token") ?? "");
    if (window.location.hash) window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true); setError(null); setMessage(null);
    const values = new FormData(event.currentTarget);
    let path = "/api/auth/login";
    let body: Record<string, unknown> = { email: values.get("email"), password: values.get("password") };
    if (mode === "register") path = "/api/auth/register";
    if (mode === "verify") { path = "/api/auth/email-verification/confirm"; body = { email: values.get("email"), token: values.get("token") }; }
    if (mode === "reset") {
      path = "/api/auth/password-reset/confirm";
      body = { email: values.get("email"), token: values.get("token"), newPassword: values.get("password"), confirmPassword: values.get("confirmPassword") };
    }
    try {
      const response = await apiMutation(path, "POST", body);
      if (!response.ok) throw new Error(await readApiError(response, text.invalid));
      if (mode === "login") {
        const requested = new URLSearchParams(window.location.search).get("return");
        const destination = requested?.startsWith("/") && !requested.startsWith("//") ? requested : localizedPath(locale, "/account");
        router.push(destination); router.refresh(); return;
      }
      setMessage(text.success);
      if (mode === "register") router.push(localizedPath(locale, "/login"));
    } catch (reason) { setError(reason instanceof Error ? reason.message : text.invalid); }
    finally { setBusy(false); }
  }

  async function requestMail(kind: "verify" | "reset", email: string) {
    setBusy(true); setError(null); setMessage(null);
    try {
      const response = await apiMutation(`/api/auth/${kind === "verify" ? "email-verification" : "password-reset"}/request`, "POST", { email });
      if (!response.ok) throw new Error(await readApiError(response, text.invalid));
      setMessage(text.genericMail);
    } catch (reason) { setError(reason instanceof Error ? reason.message : text.invalid); }
    finally { setBusy(false); }
  }

  async function deleteAccount(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError(null);
    const password = new FormData(event.currentTarget).get("password");
    try {
      const response = await apiMutation("/api/auth/account", "DELETE", { password });
      if (!response.ok) throw new Error(await readApiError(response, text.invalid));
      window.location.assign(localizedPath(locale));
    } catch (reason) { setError(reason instanceof Error ? reason.message : text.invalid); setBusy(false); }
  }

  if (mode === "account") {
    return (
      <AccountShell title={text.account}>
        {!account ? <p aria-live="polite">…</p> : !account.authenticated ? <><p>{text.signedOut}</p><p>{text.standard}</p><Link className="account-primary-link" href={localizedPath(locale, "/login")}>{text.login}</Link></> : <>
          <dl className="account-status">
            <div><dt>{text.email}</dt><dd>{account.email}</dd></div>
            <div><dt>{text.verification}</dt><dd>{account.emailVerified ? text.verified : text.unverified}</dd></div>
            <div><dt>{text.premium}</dt><dd>{account.premiumDeepActive ? text.active : text.inactive}{account.premiumExpiresAt ? ` · ${text.expires} ${new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeStyle: "short" }).format(new Date(account.premiumExpiresAt))}` : ""}</dd></div>
          </dl>
          {!account.emailVerified && <button type="button" disabled={busy} onClick={() => void requestMail("verify", account.email!)}>{text.sendVerify}</button>}
          {account.roles.includes("ADMIN") && <Link className="account-primary-link" href="/admin">{text.admin}</Link>}
          <section className="account-danger"><h2>{text.deleteTitle}</h2><p>{text.deleteHelp}</p><form onSubmit={deleteAccount}><label htmlFor="delete-password">{text.password}</label><input id="delete-password" name="password" type="password" required autoComplete="current-password"/><button disabled={busy} type="submit">{text.delete}</button></form></section>
        </>}
        <Notice error={error} message={message} />
      </AccountShell>
    );
  }

  const title = mode === "login" ? text.login : mode === "register" ? text.register : mode === "verify" ? text.verification : text.reset;
  return (
    <AccountShell title={title}>
      {account && !account.serviceAvailable && <p className="account-notice error" role="alert">{text.unavailable}</p>}
      {mode === "register" && account && !account.registrationEnabled ? <p>{text.registrationClosed}</p> :
      <form className="account-form" onSubmit={submit}>
        <label htmlFor="account-email">{text.email}</label><input id="account-email" name="email" type="email" required autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} />
        {(mode === "login" || mode === "register" || mode === "reset") && <><label htmlFor="account-password">{mode === "reset" ? text.newPassword : text.password}</label><input id="account-password" name="password" type="password" minLength={mode === "login" ? 1 : 12} required autoComplete={mode === "login" ? "current-password" : "new-password"} /></>}
        {mode === "reset" && <><label htmlFor="account-confirm">{text.confirm}</label><input id="account-confirm" name="confirmPassword" type="password" minLength={12} required autoComplete="new-password" /></>}
        {(mode === "verify" || mode === "reset") && <><label htmlFor="account-token">{text.token}</label><textarea id="account-token" name="token" required minLength={32} value={token} onChange={(event) => setToken(event.target.value)} /></>}
        <button type="submit" disabled={busy}>{busy ? "…" : text.submit}</button>
      </form>}
      {mode === "login" && <div className="account-links"><span>{text.noAccount} <Link href={localizedPath(locale, "/register")}>{text.register}</Link></span><button type="button" disabled={busy || !email} onClick={() => void requestMail("reset", email)}>{text.sendReset}</button><Link href={localizedPath(locale, "/reset-password")}>{text.reset}</Link></div>}
      {mode === "register" && <p>{text.hasAccount} <Link href={localizedPath(locale, "/login")}>{text.login}</Link></p>}
      <Notice error={error} message={message} />
    </AccountShell>
  );
}

function AccountShell({ title, children }: { title: string; children: React.ReactNode }) { return <main id="main-content" className="account-page"><article><p className="eyebrow">Tarot Destiny</p><h1>{title}</h1>{children}</article></main>; }
function Notice({ error, message }: { error: string | null; message: string | null }) { return <>{error && <p className="account-notice error" role="alert">{error}</p>}{message && <p className="account-notice success" role="status">{message}</p>}</>; }
