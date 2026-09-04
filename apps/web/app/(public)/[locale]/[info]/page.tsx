import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import InfoPage, {
  getInfoDocument,
  INFO_SLUGS,
  isInfoSlug
} from "../../../../components/info-page";
import { isLocale, LOCALES, localizedPath } from "../../../../lib/i18n";

type PageProps = { params: Promise<{ locale: string; info: string }> };

export function generateStaticParams() {
  return LOCALES.flatMap((locale) => INFO_SLUGS.map((info) => ({ locale, info })));
}

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { locale, info } = await params;
  if (!isLocale(locale) || !isInfoSlug(info)) notFound();
  const document = getInfoDocument(locale, info);
  const path = `/${info}`;

  return {
    title: document.title,
    description: document.description,
    alternates: {
      canonical: localizedPath(locale, path),
      languages: {
        th: localizedPath("th", path),
        en: localizedPath("en", path),
        "x-default": localizedPath("th", path)
      }
    },
    openGraph: {
      type: "website",
      url: localizedPath(locale, path),
      siteName: "Tarot Destiny",
      locale: locale === "th" ? "th_TH" : "en_US",
      title: document.title,
      description: document.description
    }
  };
}

export default async function LocalizedInfoPage({ params }: PageProps) {
  const { locale, info } = await params;
  if (!isLocale(locale) || !isInfoSlug(info)) notFound();
  if (info === "contact") await connection();
  return <InfoPage locale={locale} slug={info} />;
}
