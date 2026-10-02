import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ conversationId: string }> };

export async function GET(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { conversationId } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/conversations/${conversationId}/context`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/json" } });
}
