import type { Metadata } from "next";
import "./globals.css";
export const metadata: Metadata = {
  title: "SaveIT — your operational memory",
  description:
    "IT assets, documentation, and accountable changes in one place.",
};
export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
