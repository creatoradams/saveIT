import type { Metadata } from "next";
import "./globals.css";
import { connection } from "next/server";
export const metadata: Metadata = {
  title: "SaveIT — your operational memory",
  description:
    "IT assets, documentation, and accountable changes in one place.",
};
export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  await connection();
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
