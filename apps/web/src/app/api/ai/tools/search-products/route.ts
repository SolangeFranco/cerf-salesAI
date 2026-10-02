import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function GET(request: NextRequest) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const query = request.nextUrl.searchParams.get("query") ?? "";
  const response = await fetch(`${getApiBaseUrl()}/api/ai/tools/search-products?query=${encodeURIComponent(query)}`, { headers: { Authorization: `Bearer ${token}` }, cache: "no-store" });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/json" } });
}
