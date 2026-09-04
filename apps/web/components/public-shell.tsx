import Link from "next/link";
import type { ReactNode } from "react";
import { localizedPath, type Locale } from "../lib/i18n";
import LanguageLink from "./language-link";

const shellCopy = {
  en: {
    skip: "Skip to main content",
    reading: "Reading",
    guides: "Guides",
    about: "About",
    contact: "Contact",
    privacy: "Privacy",
    terms: "Terms",
    cookies: "Cookie Policy",
    disclaimer: "Disclaimer",
    consent: "Consent settings",
    notice:
      "For adults 18+. Tarot Destiny is for entertainment and self-reflection, not medical, legal, financial, or other professional advice.",
    rights: "Tarot Destiny. All rights reserved."
  },
  th: {
    skip: "ข้ามไปยังเนื้อหาหลัก",
    reading: "อ่านไพ่",
    guides: "คู่มือ",
    about: "เกี่ยวกับเรา",
    contact: "ติดต่อ",
    privacy: "ความเป็นส่วนตัว",
    terms: "ข้อกำหนด",
    cookies: "นโยบายคุกกี้",
    disclaimer: "ข้อจำกัดความรับผิด",
    consent: "ตั้งค่าความยินยอม",
    notice:
      "สำหรับผู้มีอายุ 18 ปีขึ้นไป Tarot Destiny มีไว้เพื่อความบันเทิงและการทบทวนตนเอง ไม่ใช่คำแนะนำทางการแพทย์ กฎหมาย การเงิน หรือวิชาชีพอื่น",
    rights: "Tarot Destiny สงวนลิขสิทธิ์"
  }
} as const;

export default function PublicShell({
  children,
  locale
}: {
  children: ReactNode;
  locale: Locale;
}) {
  const text = shellCopy[locale];

  return (
    <>
      <a className="skip-link" href="#main-content">{text.skip}</a>
      <header className="site-header">
        <div className="site-header-inner">
          <Link className="site-brand" href={localizedPath(locale)} aria-label="Tarot Destiny">
            <span aria-hidden="true">✦</span>
            Tarot Destiny
          </Link>
          <nav className="site-nav" aria-label={locale === "th" ? "เมนูหลัก" : "Primary navigation"}>
            <Link href={localizedPath(locale)}>{text.reading}</Link>
            <Link href={localizedPath(locale, "/guides")}>{text.guides}</Link>
            <Link href={localizedPath(locale, "/about")}>{text.about}</Link>
            <Link href={localizedPath(locale, "/contact")}>{text.contact}</Link>
          </nav>
          <LanguageLink locale={locale} />
        </div>
      </header>

      {children}

      <footer className="site-footer">
        <div className="site-footer-inner">
          <nav aria-label={locale === "th" ? "ข้อมูลและนโยบาย" : "Information and policies"}>
            <Link href={localizedPath(locale, "/about")}>{text.about}</Link>
            <Link href={localizedPath(locale, "/contact")}>{text.contact}</Link>
            <Link href={localizedPath(locale, "/privacy")}>{text.privacy}</Link>
            <Link href={localizedPath(locale, "/terms")}>{text.terms}</Link>
            <Link href={localizedPath(locale, "/cookie-policy")}>{text.cookies}</Link>
            <Link href={localizedPath(locale, "/disclaimer")}>{text.disclaimer}</Link>
            <button type="button" data-consent-settings>{text.consent}</button>
          </nav>
          <p>{text.notice}</p>
          <small>© {new Date().getUTCFullYear()} {text.rights}</small>
        </div>
      </footer>
    </>
  );
}
