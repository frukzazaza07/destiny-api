import type { ComponentType } from "react";
import type { MDXComponents } from "mdx/types";

import EnDailyOneCard, { frontmatter as EnDailyOneCardMeta } from "../content/guides/en/daily-one-card-reading.mdx";
import EnMajorMinor, { frontmatter as EnMajorMinorMeta } from "../content/guides/en/major-and-minor-arcana.mdx";
import EnCareer, { frontmatter as EnCareerMeta } from "../content/guides/en/responsible-career-readings.mdx";
import EnLove, { frontmatter as EnLoveMeta } from "../content/guides/en/responsible-love-readings.mdx";
import EnMoney, { frontmatter as EnMoneyMeta } from "../content/guides/en/responsible-money-readings.mdx";
import EnSelfReflection, { frontmatter as EnSelfReflectionMeta } from "../content/guides/en/tarot-for-self-reflection.mdx";
import EnThreeCard, { frontmatter as EnThreeCardMeta } from "../content/guides/en/three-card-tarot-spread.mdx";
import EnOrientations, { frontmatter as EnOrientationsMeta } from "../content/guides/en/upright-and-reversed-cards.mdx";
import ThDailyOneCard, { frontmatter as ThDailyOneCardMeta } from "../content/guides/th/daily-one-card-reading.mdx";
import ThMajorMinor, { frontmatter as ThMajorMinorMeta } from "../content/guides/th/major-and-minor-arcana.mdx";
import ThCareer, { frontmatter as ThCareerMeta } from "../content/guides/th/responsible-career-readings.mdx";
import ThLove, { frontmatter as ThLoveMeta } from "../content/guides/th/responsible-love-readings.mdx";
import ThMoney, { frontmatter as ThMoneyMeta } from "../content/guides/th/responsible-money-readings.mdx";
import ThSelfReflection, { frontmatter as ThSelfReflectionMeta } from "../content/guides/th/tarot-for-self-reflection.mdx";
import ThThreeCard, { frontmatter as ThThreeCardMeta } from "../content/guides/th/three-card-tarot-spread.mdx";
import ThOrientations, { frontmatter as ThOrientationsMeta } from "../content/guides/th/upright-and-reversed-cards.mdx";
import type { GuideLocale, GuideSlug } from "./guides";

export type GuideContentComponent = ComponentType<{
  readonly components?: MDXComponents;
}>;

const guideContent = {
  en: {
    "tarot-for-self-reflection": EnSelfReflection,
    "daily-one-card-reading": EnDailyOneCard,
    "three-card-tarot-spread": EnThreeCard,
    "upright-and-reversed-cards": EnOrientations,
    "major-and-minor-arcana": EnMajorMinor,
    "responsible-love-readings": EnLove,
    "responsible-career-readings": EnCareer,
    "responsible-money-readings": EnMoney
  },
  th: {
    "tarot-for-self-reflection": ThSelfReflection,
    "daily-one-card-reading": ThDailyOneCard,
    "three-card-tarot-spread": ThThreeCard,
    "upright-and-reversed-cards": ThOrientations,
    "major-and-minor-arcana": ThMajorMinor,
    "responsible-love-readings": ThLove,
    "responsible-career-readings": ThCareer,
    "responsible-money-readings": ThMoney
  }
} satisfies Record<GuideLocale, Record<GuideSlug, GuideContentComponent>>;

const guideFrontmatter = {
  en: {
    "tarot-for-self-reflection": EnSelfReflectionMeta,
    "daily-one-card-reading": EnDailyOneCardMeta,
    "three-card-tarot-spread": EnThreeCardMeta,
    "upright-and-reversed-cards": EnOrientationsMeta,
    "major-and-minor-arcana": EnMajorMinorMeta,
    "responsible-love-readings": EnLoveMeta,
    "responsible-career-readings": EnCareerMeta,
    "responsible-money-readings": EnMoneyMeta
  },
  th: {
    "tarot-for-self-reflection": ThSelfReflectionMeta,
    "daily-one-card-reading": ThDailyOneCardMeta,
    "three-card-tarot-spread": ThThreeCardMeta,
    "upright-and-reversed-cards": ThOrientationsMeta,
    "major-and-minor-arcana": ThMajorMinorMeta,
    "responsible-love-readings": ThLoveMeta,
    "responsible-career-readings": ThCareerMeta,
    "responsible-money-readings": ThMoneyMeta
  }
} satisfies Record<GuideLocale, Record<GuideSlug, Readonly<Record<string, unknown>>>>;

export function getGuideContent(
  locale: GuideLocale,
  slug: GuideSlug
): GuideContentComponent {
  return guideContent[locale][slug];
}

export function getGuideFrontmatter(
  locale: GuideLocale,
  slug: GuideSlug
): Readonly<Record<string, unknown>> {
  return guideFrontmatter[locale][slug];
}
