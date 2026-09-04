import { getGuideFrontmatter } from "./guide-content";

export const GUIDE_LOCALES = ["en", "th"] as const;

export type GuideLocale = (typeof GUIDE_LOCALES)[number];

export const GUIDE_SLUGS = [
  "tarot-for-self-reflection",
  "daily-one-card-reading",
  "three-card-tarot-spread",
  "upright-and-reversed-cards",
  "major-and-minor-arcana",
  "responsible-love-readings",
  "responsible-career-readings",
  "responsible-money-readings",
] as const;

export type GuideSlug = (typeof GUIDE_SLUGS)[number];
export type GuideReviewStatus = "needs-owner-review" | "reviewed";

export interface GuideMetadata {
  readonly title: string;
  readonly description: string;
  readonly slug: GuideSlug;
  readonly locale: GuideLocale;
  readonly translationKey: GuideSlug;
  readonly author: string;
  readonly publishedAt: string | null;
  readonly updatedAt: string;
  readonly draft: boolean;
  readonly reviewStatus: GuideReviewStatus;
  readonly canonicalPath: string;
  readonly sourcePath: string;
}

interface LocalizedGuideCopy {
  readonly title: string;
  readonly description: string;
}

interface GuideDefinition {
  readonly slug: GuideSlug;
  readonly en: LocalizedGuideCopy;
  readonly th: LocalizedGuideCopy;
}

const AUTHOR = "Destiny Tarot Editorial Team";
const UPDATED_AT = "2026-09-03";

const GUIDE_DEFINITIONS: readonly GuideDefinition[] = [
  {
    slug: "tarot-for-self-reflection",
    en: {
      title: "Tarot for Self-Reflection: A Grounded Practice",
      description:
        "Use tarot imagery as a structured prompt for noticing feelings, assumptions, choices, and next steps without treating cards as fixed predictions.",
    },
    th: {
      title: "ใช้ไพ่ทาโรต์เพื่อทบทวนตนเองอย่างมีหลักยึด",
      description:
        "แนวทางใช้ภาพและสัญลักษณ์บนไพ่เพื่อสำรวจความรู้สึก สมมติฐาน ทางเลือก และก้าวถัดไป โดยไม่มองไพ่เป็นคำทำนายตายตัว",
    },
  },
  {
    slug: "daily-one-card-reading",
    en: {
      title: "A Thoughtful Daily One-Card Tarot Practice",
      description:
        "Build a brief one-card routine for reflection, journaling, and intentional action while avoiding dependency and repetitive reassurance seeking.",
    },
    th: {
      title: "ฝึกอ่านไพ่หนึ่งใบประจำวันอย่างใคร่ครวญ",
      description:
        "สร้างกิจวัตรไพ่หนึ่งใบเพื่อทบทวน เขียนบันทึก และลงมืออย่างมีเจตนา พร้อมหลีกเลี่ยงการพึ่งพาไพ่หรือถามซ้ำเพื่อขอความมั่นใจ",
    },
  },
  {
    slug: "three-card-tarot-spread",
    en: {
      title: "Three-Card Tarot Spreads for Clearer Reflection",
      description:
        "Learn flexible three-card layouts that turn a broad concern into useful observations, options, and practical next steps.",
    },
    th: {
      title: "ผังไพ่ทาโรต์สามใบเพื่อการทบทวนที่ชัดเจนขึ้น",
      description:
        "เรียนรู้ผังไพ่สามใบที่ยืดหยุ่น ช่วยเปลี่ยนความกังวลกว้าง ๆ ให้เป็นข้อสังเกต ทางเลือก และก้าวถัดไปที่นำไปใช้ได้",
    },
  },
  {
    slug: "upright-and-reversed-cards",
    en: {
      title: "Reading Upright and Reversed Tarot Cards",
      description:
        "A practical framework for interpreting card orientation as emphasis, blockage, internal experience, or alternative perspective rather than good or bad fate.",
    },
    th: {
      title: "อ่านไพ่ตั้งตรงและไพ่กลับหัวอย่างเป็นระบบ",
      description:
        "กรอบคิดที่ใช้ทิศทางไพ่เพื่อสำรวจการเน้นย้ำ สิ่งติดขัด ประสบการณ์ภายใน หรือมุมมองอีกด้าน แทนการตัดสินว่าเป็นโชคดีหรือโชคร้าย",
    },
  },
  {
    slug: "major-and-minor-arcana",
    en: {
      title: "Major and Minor Arcana: A Practical Map",
      description:
        "Understand how the Major Arcana, suits, numbers, and court cards work together as a flexible vocabulary for reflective tarot reading.",
    },
    th: {
      title: "ไพ่ชุดใหญ่และชุดเล็ก: แผนที่สำหรับการอ่านอย่างเข้าใจ",
      description:
        "ทำความเข้าใจว่าไพ่ชุดใหญ่ ธาตุ ตัวเลข และไพ่บุคคลทำงานร่วมกันอย่างไรในฐานะภาษาที่ยืดหยุ่นสำหรับการทบทวนตนเอง",
    },
  },
  {
    slug: "responsible-love-readings",
    en: {
      title: "Responsible Tarot Readings About Love",
      description:
        "Ask relationship questions that respect consent, uncertainty, privacy, and personal agency while focusing on communication and healthy choices.",
    },
    th: {
      title: "อ่านไพ่เรื่องความรักอย่างรับผิดชอบ",
      description:
        "ตั้งคำถามเรื่องความสัมพันธ์โดยเคารพความยินยอม ความไม่แน่นอน ความเป็นส่วนตัว และสิทธิในการตัดสินใจ พร้อมมุ่งที่การสื่อสารและทางเลือกที่ดีต่อใจ",
    },
  },
  {
    slug: "responsible-career-readings",
    en: {
      title: "Responsible Tarot Readings About Career",
      description:
        "Use tarot to clarify values, strengths, tradeoffs, and experiments at work without replacing evidence, professional advice, or practical planning.",
    },
    th: {
      title: "อ่านไพ่เรื่องอาชีพอย่างรับผิดชอบ",
      description:
        "ใช้ไพ่เพื่อชี้แจงคุณค่า จุดแข็ง ข้อแลกเปลี่ยน และการทดลองเล็ก ๆ ในงาน โดยไม่แทนที่ข้อมูล คำแนะนำจากผู้เชี่ยวชาญ หรือการวางแผนจริง",
    },
  },
  {
    slug: "responsible-money-readings",
    en: {
      title: "Responsible Tarot Readings About Money",
      description:
        "Reflect on money habits, emotions, priorities, and questions to research without using tarot as financial advice or a prediction of gains and losses.",
    },
    th: {
      title: "อ่านไพ่เรื่องการเงินอย่างรับผิดชอบ",
      description:
        "ทบทวนนิสัย อารมณ์ ลำดับความสำคัญ และเรื่องที่ควรค้นคว้าเกี่ยวกับเงิน โดยไม่ใช้ไพ่แทนคำแนะนำทางการเงินหรือทำนายกำไรขาดทุน",
    },
  },
];

function makeEntry(
  definition: GuideDefinition,
  locale: GuideLocale,
): GuideMetadata {
  const metadata = getGuideFrontmatter(locale, definition.slug);
  const canonicalPath = "/" + locale + "/guides/" + definition.slug;
  const key = locale + ":" + definition.slug;
  const title = requiredString(metadata.title, key + ".title");
  const description = requiredString(metadata.description, key + ".description");
  const author = requiredString(metadata.author, key + ".author");
  const updatedAt = requiredDate(metadata.updatedAt, key + ".updatedAt");
  const publishedAt = nullableDate(metadata.publishedAt, key + ".publishedAt");
  const draft = requiredBoolean(metadata.draft, key + ".draft");
  const reviewStatus = requiredReviewStatus(
    metadata.reviewStatus,
    key + ".reviewStatus"
  );

  if (metadata.slug !== definition.slug || metadata.translationKey !== definition.slug) {
    throw new Error(key + " has an invalid slug or translation key.");
  }
  if (metadata.locale !== locale || metadata.canonicalPath !== canonicalPath) {
    throw new Error(key + " has an invalid locale or canonical path.");
  }
  if (draft === (reviewStatus === "reviewed" && publishedAt !== null)) {
    throw new Error(key + " has inconsistent review/publication state.");
  }

  return {
    title,
    description,
    slug: definition.slug,
    locale,
    translationKey: definition.slug,
    author,
    publishedAt,
    updatedAt,
    draft,
    reviewStatus,
    canonicalPath,
    sourcePath:
      "content/guides/" + locale + "/" + definition.slug + ".mdx",
  };
}

function requiredString(value: unknown, field: string): string {
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(field + " must be a non-empty string.");
  }
  return value;
}

function requiredBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") {
    throw new Error(field + " must be a boolean.");
  }
  return value;
}

function requiredDate(value: unknown, field: string): string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    throw new Error(field + " must use YYYY-MM-DD.");
  }
  return value;
}

function nullableDate(value: unknown, field: string): string | null {
  if (value === null) return null;
  return requiredDate(value, field);
}

function requiredReviewStatus(
  value: unknown,
  field: string
): GuideReviewStatus {
  if (value !== "needs-owner-review" && value !== "reviewed") {
    throw new Error(field + " has an unsupported value.");
  }
  return value;
}

export const guideCatalog: readonly GuideMetadata[] =
  GUIDE_DEFINITIONS.flatMap((definition) =>
    GUIDE_LOCALES.map((locale) => makeEntry(definition, locale)),
  );

export function isGuidePublishable(guide: GuideMetadata): boolean {
  return (
    !guide.draft &&
    guide.reviewStatus === "reviewed" &&
    guide.publishedAt !== null
  );
}

export function getGuides(
  locale: GuideLocale,
  options: { readonly includeDrafts?: boolean } = {},
): readonly GuideMetadata[] {
  return guideCatalog.filter(
    (guide) =>
      guide.locale === locale &&
      (options.includeDrafts || isGuidePublishable(guide)),
  );
}

export function getGuide(
  locale: GuideLocale,
  slug: string,
  options: { readonly includeDrafts?: boolean } = {},
): GuideMetadata | undefined {
  return guideCatalog.find(
    (guide) =>
      guide.locale === locale &&
      guide.slug === slug &&
      (options.includeDrafts || isGuidePublishable(guide)),
  );
}

export function getPublishedGuidePaths(): readonly string[] {
  return guideCatalog
    .filter(isGuidePublishable)
    .map((guide) => guide.canonicalPath);
}

export function getGuideAlternates(
  slug: GuideSlug,
): Readonly<Record<GuideLocale, string>> {
  return {
    en: "/en/guides/" + slug,
    th: "/th/guides/" + slug,
  };
}

export function validateGuideCatalog(): readonly string[] {
  const errors: string[] = [];
  const expectedEntryCount = GUIDE_SLUGS.length * GUIDE_LOCALES.length;

  if (guideCatalog.length !== expectedEntryCount) {
    errors.push(
      "Expected " +
        expectedEntryCount +
        " localized guide entries, found " +
        guideCatalog.length +
        ".",
    );
  }

  const keys = new Set<string>();
  for (const guide of guideCatalog) {
    const key = guide.locale + ":" + guide.slug;
    if (keys.has(key)) {
      errors.push("Duplicate guide entry: " + key + ".");
    }
    keys.add(key);

    if (guide.translationKey !== guide.slug) {
      errors.push(key + " has a mismatched translation key.");
    }
    if (guide.canonicalPath !== "/" + guide.locale + "/guides/" + guide.slug) {
      errors.push(key + " has an invalid canonical path.");
    }
    if (guide.sourcePath !== "content/guides/" + guide.locale + "/" + guide.slug + ".mdx") {
      errors.push(key + " has an invalid source path.");
    }
    if (guide.reviewStatus !== "reviewed" && !guide.draft) {
      errors.push(key + " cannot be published before owner review.");
    }
    if (guide.draft && guide.publishedAt !== null) {
      errors.push(key + " is a draft but has a publication date.");
    }
    if (!guide.draft && guide.publishedAt === null) {
      errors.push(key + " is published but has no publication date.");
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(guide.updatedAt)) {
      errors.push(key + " has an invalid update date.");
    }
  }

  for (const slug of GUIDE_SLUGS) {
    for (const locale of GUIDE_LOCALES) {
      if (!keys.has(locale + ":" + slug)) {
        errors.push("Missing " + locale + " translation for " + slug + ".");
      }
    }
  }

  return errors;
}

export function assertValidGuideCatalog(): void {
  const errors = validateGuideCatalog();
  if (errors.length > 0) {
    throw new Error("Invalid guide catalog:\n- " + errors.join("\n- "));
  }
}

export function validateGuidePublicationReadiness(): readonly string[] {
  const errors = [...validateGuideCatalog()];

  for (const guide of guideCatalog) {
    if (!isGuidePublishable(guide)) {
      errors.push(
        guide.locale +
          ":" +
          guide.slug +
          " is not publication-ready; owner review, draft removal, and a publication date are required.",
      );
    }
  }

  const publishedPairs = GUIDE_SLUGS.filter((slug) =>
    GUIDE_LOCALES.every((locale) => {
      const guide = guideCatalog.find(
        (entry) => entry.locale === locale && entry.slug === slug,
      );
      return guide !== undefined && isGuidePublishable(guide);
    }),
  );

  if (publishedPairs.length !== GUIDE_SLUGS.length) {
    errors.push(
      "Expected exactly " +
        GUIDE_SLUGS.length +
        " publication-ready bilingual pairs, found " +
        publishedPairs.length +
        ".",
    );
  }

  return errors;
}

export function assertGuidePublicationReady(): void {
  const errors = validateGuidePublicationReadiness();
  if (errors.length > 0) {
    throw new Error(
      "Guide publication readiness check failed:\n- " + errors.join("\n- "),
    );
  }
}
