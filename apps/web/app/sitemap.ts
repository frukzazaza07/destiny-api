import type { MetadataRoute } from "next";
import { connection } from "next/server";

import { INFO_SLUGS } from "../components/info-page";
import { GUIDE_SLUGS, getGuide } from "../lib/guides";
import { localizedPath } from "../lib/i18n";
import { getMonetizationRuntimeConfig } from "../lib/monetization-runtime";

const LAST_SITE_UPDATE = "2026-09-03";

export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  await connection();
  const runtime = getMonetizationRuntimeConfig();
  if (!runtime.production || !runtime.siteUrl) return [];

  const origin = runtime.siteUrl;
  const paths = ["", "/guides", ...INFO_SLUGS.map((slug) => `/${slug}`)];
  const staticEntries: MetadataRoute.Sitemap = paths.flatMap((path) =>
    (["th", "en"] as const).map((locale) => ({
      url: `${origin}${localizedPath(locale, path)}`,
      lastModified: LAST_SITE_UPDATE,
      changeFrequency: path === "/guides" ? "weekly" as const : "monthly" as const,
      priority: path === "" ? 1 : path === "/guides" ? 0.8 : 0.5,
      alternates: {
        languages: {
          th: `${origin}${localizedPath("th", path)}`,
          en: `${origin}${localizedPath("en", path)}`,
          "x-default": `${origin}${localizedPath("th", path)}`
        }
      }
    }))
  );

  const guideEntries: MetadataRoute.Sitemap = GUIDE_SLUGS.flatMap((slug) => {
    const th = getGuide("th", slug);
    const en = getGuide("en", slug);
    if (!th || !en) return [];

    return [th, en].map((guide) => ({
      url: `${origin}${guide.canonicalPath}`,
      lastModified: guide.updatedAt,
      changeFrequency: "monthly" as const,
      priority: 0.7,
      alternates: {
        languages: {
          th: `${origin}${th.canonicalPath}`,
          en: `${origin}${en.canonicalPath}`,
          "x-default": `${origin}${th.canonicalPath}`
        }
      }
    }));
  });

  return [...staticEntries, ...guideEntries];
}
