import { connection } from "next/server";

import { getMonetizationRuntimeConfig } from "../../lib/monetization-runtime";

export async function GET() {
  await connection();
  const sellerLine = getMonetizationRuntimeConfig().adsense.adsTxtLine;

  return new Response(sellerLine ? `${sellerLine}\n` : "", {
    status: 200,
    headers: {
      "Cache-Control": "no-store",
      "Content-Type": "text/plain; charset=utf-8",
      "X-Content-Type-Options": "nosniff"
    }
  });
}
