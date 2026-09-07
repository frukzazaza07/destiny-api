import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import ImmersiveReadingJourney from "../../../components/immersive-reading-journey";
import { getGuides } from "../../../lib/guides";
import { isLocale, localizedPath, type Locale } from "../../../lib/i18n";
import { getMonetizationRuntimeConfig } from "../../../lib/monetization-runtime";

const homeCopy = {
  en: {
    title: "Reflective online tarot reading",
    description: "A thoughtful tarot reading tool and practical guides for self-reflection.",
    heading: "Use tarot as a prompt for clearer reflection",
    introduction:
      "Tarot Destiny pairs a simple card-selection experience with grounded explanations. Notice patterns, name your choices, and decide what practical step makes sense to you.",
    howTitle: "A responsible way to use this reading",
    steps: [
      "Choose a daily card or three-card spread and focus on one question.",
      "Treat the interpretation as a perspective to consider, not a fixed prediction.",
      "Check important decisions against reliable evidence and qualified professional advice."
    ],
    guideTitle: "Learn before you read",
    guideIntro: "Explore our bilingual guides to understand the cards and use them responsibly.",
    allGuides: "View all guides",
    guidesPending: "The first eight guide translations are undergoing owner review and will appear here once approved.",
    boundaryTitle: "Important boundaries",
    boundary:
      "This service is for adults aged 18 and over. It is provided for entertainment and self-reflection only and does not provide medical, mental-health, legal, financial, or other professional advice."
  },
  th: {
    title: "อ่านไพ่ทาโรต์ออนไลน์เพื่อทบทวนตนเอง",
    description: "เครื่องมืออ่านไพ่ทาโรต์อย่างใคร่ครวญ พร้อมคู่มือเพื่อการทบทวนตนเองอย่างรับผิดชอบ",
    heading: "ใช้ไพ่ทาโรต์เป็นคำชวนคิดเพื่อมองเรื่องต่าง ๆ ให้ชัดขึ้น",
    introduction:
      "Tarot Destiny ผสานประสบการณ์เลือกไพ่ที่เรียบง่ายกับคำอธิบายที่มีหลักยึด เพื่อช่วยให้คุณสังเกตรูปแบบ มองเห็นทางเลือก และตัดสินใจก้าวถัดไปด้วยตนเอง",
    howTitle: "แนวทางใช้งานอย่างรับผิดชอบ",
    steps: [
      "เลือกไพ่ประจำวันหนึ่งใบหรือผังสามใบ แล้วจดจ่อกับคำถามหนึ่งเรื่อง",
      "มองคำตีความเป็นอีกมุมหนึ่งสำหรับพิจารณา ไม่ใช่คำทำนายที่ตายตัว",
      "ตรวจสอบการตัดสินใจสำคัญกับข้อมูลที่เชื่อถือได้และผู้เชี่ยวชาญที่เหมาะสม"
    ],
    guideTitle: "เรียนรู้ก่อนเริ่มอ่านไพ่",
    guideIntro: "อ่านคู่มือสองภาษาของเราเพื่อทำความเข้าใจไพ่และใช้อย่างรับผิดชอบ",
    allGuides: "ดูคู่มือทั้งหมด",
    guidesPending: "คู่มือแปดเรื่องแรกทั้งสองภาษากำลังรอเจ้าของตรวจทาน และจะแสดงที่นี่เมื่ออนุมัติแล้ว",
    boundaryTitle: "ขอบเขตสำคัญ",
    boundary:
      "บริการนี้สำหรับผู้มีอายุ 18 ปีขึ้นไป จัดทำเพื่อความบันเทิงและการทบทวนตนเองเท่านั้น ไม่ใช่คำแนะนำทางการแพทย์ สุขภาพจิต กฎหมาย การเงิน หรือวิชาชีพอื่น"
  }
} as const;

type PageProps = { params: Promise<{ locale: string }> };

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  await connection();
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const text = homeCopy[locale];
  const runtime = getMonetizationRuntimeConfig();
  const siteOrigin = runtime.siteUrl ?? "http://localhost:3000";
  const canonicalUrl = `${siteOrigin}${localizedPath(locale)}`;

  return {
    title: text.title,
    description: text.description,
    alternates: {
      canonical: canonicalUrl,
      languages: {
        th: `${siteOrigin}/th/`,
        en: `${siteOrigin}/en/`,
        "x-default": `${siteOrigin}/th/`
      }
    },
    openGraph: {
      type: "website",
      url: canonicalUrl,
      siteName: "Tarot Destiny",
      locale: locale === "th" ? "th_TH" : "en_US",
      title: text.title,
      description: text.description
    }
  };
}

export default async function LocalizedReadingPage({ params }: PageProps) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();

  return (
    <main id="main-content">
      <ImmersiveReadingJourney initialLocale={locale} />
      <HomePublisherContent locale={locale} />
    </main>
  );
}

function HomePublisherContent({ locale }: { locale: Locale }) {
  const text = homeCopy[locale];
  const guides = getGuides(locale);

  return (
    <section className="publisher-content" aria-labelledby="publisher-heading">
      <div className="publisher-intro">
        <p className="eyebrow">Tarot Destiny</p>
        <h2 id="publisher-heading">{text.heading}</h2>
        <p>{text.introduction}</p>
      </div>

      <section>
        <h3>{text.howTitle}</h3>
        <ol>
          {text.steps.map((step) => <li key={step}>{step}</li>)}
        </ol>
      </section>

      <section>
        <h3>{text.guideTitle}</h3>
        <p>{text.guideIntro}</p>
        {guides.length > 0 ? (
          <>
            <ul className="home-guide-links">
              {guides.map((guide) => (
                <li key={guide.slug}>
                  <Link href={guide.canonicalPath}>{guide.title}</Link>
                </li>
              ))}
            </ul>
            <Link className="text-link" href={localizedPath(locale, "/guides")}>{text.allGuides} →</Link>
          </>
        ) : (
          <p className="guides-pending" role="status">{text.guidesPending}</p>
        )}
      </section>

      <aside className="publisher-boundary" aria-labelledby="boundary-heading">
        <h3 id="boundary-heading">{text.boundaryTitle}</h3>
        <p>{text.boundary}</p>
        <Link href={localizedPath(locale, "/disclaimer")}>
          {locale === "th" ? "อ่านข้อจำกัดความรับผิด" : "Read the full disclaimer"}
        </Link>
      </aside>
    </section>
  );
}
