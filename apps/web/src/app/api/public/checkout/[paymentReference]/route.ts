import { NextResponse } from "next/server";
import { getApiBaseUrl } from "@/lib/session";

export async function GET(_: Request, context: { params: Promise<{ paymentReference: string }> }) {
  const { paymentReference } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/public/checkout/${encodeURIComponent(paymentReference)}`, { cache: "no-store" });
  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}