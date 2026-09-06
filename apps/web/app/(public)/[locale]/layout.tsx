import type { Metadata } from "next";
import { headers } from "next/headers";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { ConsentProvider } from "../../../components/monetization";
import PublicShell from "../../../components/public-shell";
import { isLocale, LOCALES } from "../../../lib/i18n";
import { getMonetizationRuntimeConfig } from "../../../lib/monetization-runtime";
import { resolvePrivacyRegion } from "../../../lib/privacy-region";
import "../../globals.css";

export async function generateMetadata(): Promise<Metadata> {
  await connection();
  const runtime = getMonetizationRuntimeConfig();
  const mayIndex = runtime.production && runtime.siteUrl !== null;

  return {
    metadataBase: new URL(runtime.siteUrl ?? "http://localhost:3000"),
    applicationName: "Tarot Destiny",
    title: {
      default: "Tarot Destiny",
      template: "%s | Tarot Destiny"
    },
    robots: {
      index: mayIndex,
      follow: mayIndex,
      noarchive: !mayIndex
    },
    ...(runtime.adsense.verificationMeta
      ? {
          other: {
            "google-adsense-account": runtime.adsense.verificationMeta
          }
        }
      : {})
  };
}

export function generateStaticParams() {
  return LOCALES.map((locale) => ({ locale }));
}

export default async function LocalizedRootLayout({
  children,
  params
}: Readonly<{
  children: React.ReactNode;
  params: Promise<{ locale: string }>;
}>) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const runtime = getMonetizationRuntimeConfig();
  const requestHeaders = await headers();
  const privacy = resolvePrivacyRegion(
    requestHeaders,
    runtime.trustCloudflareCountryHeader
  );

  return (
    <html lang={locale}>
      <body>
        <ConsentProvider
          locale={locale}
          region={privacy.region}
          googleCmpConfigured={
            runtime.adsense.publisherId !== null &&
            (runtime.adsense.enabled || runtime.analytics.enabled || runtime.rewardedAdsEnabled)
          }
          services={{
            advertising: runtime.adsense.enabled || runtime.rewardedAdsEnabled,
            analytics: runtime.analytics.enabled
          }}
        >
          <PublicShell locale={locale}>{children}</PublicShell>
        </ConsentProvider>
      </body>
    </html>
  );
}
