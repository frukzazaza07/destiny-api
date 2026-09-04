import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import type { MDXComponents } from "mdx/types";

import {
  AdsenseGuideUnit,
  EeaGoogleCmp,
  GoogleAnalyticsPageView
} from "../../../../../components/monetization";
import { getGuideContent } from "../../../../../lib/guide-content";
import {
  getGuide,
  getGuideAlternates,
  guideCatalog,
  isGuidePublishable
} from "../../../../../lib/guides";
import { isLocale, localizedPath, type Locale } from "../../../../../lib/i18n";
import { getMonetizationRuntimeConfig } from "../../../../../lib/monetization-runtime";

type PageProps = { params: Promise<{ locale: string; slug: string }> };

export const dynamicParams = false;

export function generateStaticParams() {
  return guideCatalog
    .filter(isGuidePublishable)
    .map((guide) => ({ locale: guide.locale, slug: guide.slug }));
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { locale, slug } = await params;
  if (!isLocale(locale)) notFound();
  const guide = getGuide(locale, slug);
  if (!guide) notFound();
  const alternates = getGuideAlternates(guide.slug);

  return {
    title: guide.title,
    description: guide.description,
    authors: [{ name: guide.author }],
    alternates: {
      canonical: guide.canonicalPath,
      languages: {
        th: alternates.th,
        en: alternates.en,
        "x-default": alternates.th
      }
    },
    openGraph: {
      type: "article",
      url: guide.canonicalPath,
      siteName: "Tarot Destiny",
      locale: locale === "th" ? "th_TH" : "en_US",
      title: guide.title,
      description: guide.description,
      publishedTime: guide.publishedAt ?? undefined,
      modifiedTime: guide.updatedAt,
      authors: [guide.author]
    }
  };
}

export default async function GuidePage({ params }: PageProps) {
  await connection();
  const { locale, slug } = await params;
  if (!isLocale(locale)) notFound();
  const guide = getGuide(locale, slug);
  if (!guide || !guide.publishedAt) notFound();

  const runtime = getMonetizationRuntimeConfig();
  const GuideContent = getGuideContent(locale, guide.slug);
  const alternateLocale: Locale = locale === "th" ? "en" : "th";
  const alternate = getGuide(alternateLocale, guide.slug);
  const components: MDXComponents = {
    GuideAd: () => (
      <AdsenseGuideUnit
        clientId={runtime.adsense.clientId}
        eligibleGuidePage
        enabled={runtime.adsense.enabled}
        locale={locale}
        slotId={runtime.adsense.guideSlotId}
      />
    )
  };
  const canonicalUrl = runtime.siteUrl
    ? `${runtime.siteUrl}${guide.canonicalPath}`
    : null;
  const structuredData = canonicalUrl
    ? {
        "@context": "https://schema.org",
        "@type": "Article",
        headline: guide.title,
        description: guide.description,
        inLanguage: locale,
        datePublished: guide.publishedAt,
        dateModified: guide.updatedAt,
        author: { "@type": "Organization", name: guide.author },
        publisher: { "@type": "Organization", name: "Tarot Destiny" },
        mainEntityOfPage: canonicalUrl
      }
    : null;

  return (
    <main id="main-content" className="guide-page">
      <EeaGoogleCmp
        eligibleGuidePage
        enabled={runtime.adsense.enabled || runtime.analytics.enabled}
        publisherId={runtime.adsense.publisherId}
      />
      <GoogleAnalyticsPageView
        eligibleGuidePage
        enabled={runtime.analytics.enabled}
        locale={locale}
        measurementId={runtime.analytics.measurementId}
      />
      {structuredData ? (
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: JSON.stringify(structuredData).replace(/</g, "\\u003c")
          }}
        />
      ) : null}

      <article>
        <header className="guide-header">
          <p className="eyebrow">Tarot Destiny</p>
          <h1>{guide.title}</h1>
          <p>{guide.description}</p>
          <dl className="guide-byline">
            <div><dt>{locale === "th" ? "ผู้เขียน" : "Author"}</dt><dd>{guide.author}</dd></div>
            <div><dt>{locale === "th" ? "เผยแพร่" : "Published"}</dt><dd>{guide.publishedAt}</dd></div>
            <div><dt>{locale === "th" ? "ปรับปรุง" : "Updated"}</dt><dd>{guide.updatedAt}</dd></div>
          </dl>
          {alternate ? (
            <Link href={alternate.canonicalPath} hrefLang={alternateLocale}>
              {locale === "th" ? "Read in English" : "อ่านภาษาไทย"}
            </Link>
          ) : null}
        </header>

        <div className="guide-prose">
          <GuideContent components={components} />
        </div>

        <footer className="guide-footer">
          <p>
            {locale === "th"
              ? "เนื้อหานี้มีไว้เพื่อความบันเทิงและการทบทวนตนเองของผู้มีอายุ 18 ปีขึ้นไป ไม่ใช่คำแนะนำจากผู้เชี่ยวชาญ"
              : "For adults 18+. This article is for entertainment and self-reflection, not professional advice."}
          </p>
          <Link href={localizedPath(locale, "/guides")}>
            {locale === "th" ? "กลับไปที่คู่มือทั้งหมด" : "Back to all guides"}
          </Link>
        </footer>
      </article>
    </main>
  );
}
