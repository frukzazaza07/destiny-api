import { notFound } from "next/navigation";
import AccountClient from "../../../../components/account-client";
import { isLocale } from "../../../../lib/i18n";

export const metadata = { robots: { index: false, follow: false } };

export default async function ResetPasswordPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  return <AccountClient locale={locale} mode="reset" />;
}
