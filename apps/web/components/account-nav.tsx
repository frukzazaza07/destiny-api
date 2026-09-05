"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { LogIn, LogOut, UserRound } from "lucide-react";
import { apiFetch, apiMutation, readApiData } from "../lib/api-client";
import { localizedPath, type Locale } from "../lib/i18n";

type Account = { authenticated: boolean; email: string | null };

export default function AccountNav({ locale }: { locale: Locale }) {
  const [account, setAccount] = useState<Account | null>(null);

  useEffect(() => {
    let active = true;
    apiFetch("/api/auth/me", { cache: "no-store" })
      .then((response) => readApiData<Account>(response))
      .then((value) => { if (active) setAccount(value); })
      .catch(() => { if (active) setAccount({ authenticated: false, email: null }); });
    return () => { active = false; };
  }, []);

  async function logout() {
    const response = await apiMutation("/api/auth/logout", "POST", {});
    if (response.ok) {
      setAccount({ authenticated: false, email: null });
      window.location.assign(localizedPath(locale));
    }
  }

  if (!account) return <span className="account-nav-placeholder" aria-hidden="true" />;
  if (!account.authenticated) {
    return <Link className="account-nav" href={localizedPath(locale, "/login")}><LogIn size={15} />{locale === "th" ? "เข้าสู่ระบบ" : "Log in"}</Link>;
  }

  return (
    <span className="account-nav-group">
      <Link className="account-nav" href={localizedPath(locale, "/account")} title={account.email ?? undefined}>
        <UserRound size={15} />{locale === "th" ? "บัญชี" : "Account"}
      </Link>
      <button className="account-nav" type="button" onClick={() => void logout()}>
        <LogOut size={15} />{locale === "th" ? "ออก" : "Log out"}
      </button>
    </span>
  );
}
