import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ paymentId: string }> };

export async function POST(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { paymentId } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/payments/${paymentId}/manual-confirm`, { method: "POST", headers: { Authorization: `Bearer ${token}` } });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/json" } });
}