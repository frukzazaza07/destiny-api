"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { localizedPath, otherLocale, type Locale } from "../lib/i18n";

export default function LanguageLink({ locale }: { locale: Locale }) {
  const pathname = usePathname();
  const targetLocale = otherLocale(locale);
  const targetPath = pathname.replace(/^\/(?:th|en)(?=\/|$)/, `/${targetLocale}`);

  return (
    <Link
      className="language-link"
      href={targetPath === pathname ? localizedPath(targetLocale) : targetPath}
      hrefLang={targetLocale}
      lang={targetLocale}
    >
      {targetLocale === "th" ? "ไทย" : "English"}
    </Link>
  );
}
