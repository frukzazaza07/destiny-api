import type { Metadata } from "next";
import "../../globals.css";

export const metadata: Metadata = {
  title: "Operations | Tarot Destiny",
  robots: {
    index: false,
    follow: false,
    noarchive: true,
    nosnippet: true
  }
};

export default function AdminRootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
