import type { MetadataRoute } from "next";
import { connection } from "next/server";

import { getMonetizationRuntimeConfig } from "../lib/monetization-runtime";

export default async function robots(): Promise<MetadataRoute.Robots> {
  await connection();
  const runtime = getMonetizationRuntimeConfig();

  if (!runtime.production || !runtime.siteUrl) {
    return {
      rules: { userAgent: "*", disallow: "/" }
    };
  }

  return {
    rules: {
      userAgent: "*",
      allow: "/",
      disallow: ["/admin", "/admin/"]
    },
    sitemap: `${runtime.siteUrl}/sitemap.xml`,
    host: runtime.siteUrl
  };
}
