import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "CloudPulse APM - Next.js Observability Dashboard",
  description: "Distributed APM and Anomaly Detection Platform built on .NET 10 Microservices",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" className="dark">
      <body className="bg-darkbg text-gray-100 min-h-screen antialiased">
        {children}
      </body>
    </html>
  );
}
