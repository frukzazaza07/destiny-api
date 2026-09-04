import type { Metadata } from "next";
import "../globals.css";

export const metadata: Metadata = {
  title: "Tarot Destiny"
};

export default function RootLayout({
  children
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="th">
      <body>{children}</body>
    </html>
  );
}
