import { NextResponse } from "next/server";
import { getApiBaseUrl } from "@/lib/session";

export async function GET(_: Request, context: { params: Promise<{ slug: string }> }) {
  const { slug } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/public/store/${encodeURIComponent(slug)}`, { cache: "no-store" });
  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}

export async function POST(request: Request, context: { params: Promise<{ slug: string }> }) {
  const { slug } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/public/store/${encodeURIComponent(slug)}/orders`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: await request.text(),
  });
  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}