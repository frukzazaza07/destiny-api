import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";

import { getGuides } from "../../../../lib/guides";
import { isLocale, localizedPath, type Locale } from "../../../../lib/i18n";

type PageProps = { params: Promise<{ locale: string }> };

const copy = {
  en: {
    title: "Responsible tarot guides",
    description:
      "Practical, grounded guides for using tarot as adult entertainment and self-reflection.",
    introduction:
      "These guides explain tarot in plain language while preserving uncertainty, evidence, consent, and personal agency.",
    pending:
      "Eight bilingual guide pairs are prepared but remain private until the owner has reviewed and approved every article.",
    updated: "Updated",
    read: "Read guide"
  },
  th: {
    title: "คู่มือการใช้ไพ่ทาโรต์อย่างรับผิดชอบ",
    description:
      "คู่มือที่นำไปใช้ได้จริงสำหรับการใช้ไพ่ทาโรต์เพื่อความบันเทิงและการทบทวนตนเองของผู้ใหญ่",
    introduction:
      "คู่มือเหล่านี้อธิบายไพ่ทาโรต์ด้วยภาษาที่เข้าใจง่าย โดยคำนึงถึงความไม่แน่นอน หลักฐาน ความยินยอม และสิทธิในการตัดสินใจของแต่ละคน",
    pending:
      "คู่มือสองภาษาจำนวนแปดคู่จัดทำเป็นฉบับร่างแล้ว แต่จะยังไม่เผยแพร่จนกว่าเจ้าของจะตรวจทานและอนุมัติทุกบทความ",
    updated: "ปรับปรุง",
    read: "อ่านคู่มือ"
  }
} as const;

export async function generateMetadata({ params }: PageProps): Promise<Metadata> {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const text = copy[locale];
  const canonical = localizedPath(locale, "/guides");

  return {
    title: text.title,
    description: text.description,
    alternates: {
      canonical,
      languages: {
        th: "/th/guides",
        en: "/en/guides",
        "x-default": "/th/guides"
      }
    },
    openGraph: {
      type: "website",
      url: canonical,
      siteName: "Tarot Destiny",
      locale: locale === "th" ? "th_TH" : "en_US",
      title: text.title,
      description: text.description
    }
  };
}

export default async function GuidesPage({ params }: PageProps) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const text = copy[locale];
  const guides = getGuides(locale);

  return (
    <main id="main-content" className="guide-index">
      <header>
        <p className="eyebrow">Tarot Destiny</p>
        <h1>{text.title}</h1>
        <p>{text.introduction}</p>
      </header>

      {guides.length === 0 ? (
        <p className="guides-pending" role="status">{text.pending}</p>
      ) : (
        <ul className="guide-cards">
          {guides.map((guide) => (
            <li key={guide.slug}>
              <article>
                <h2><Link href={guide.canonicalPath}>{guide.title}</Link></h2>
                <p>{guide.description}</p>
                <small>{text.updated}: {guide.updatedAt}</small>
                <Link className="text-link" href={guide.canonicalPath}>{text.read} →</Link>
              </article>
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
