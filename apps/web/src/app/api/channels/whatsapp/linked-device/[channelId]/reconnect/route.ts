import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function POST(request: NextRequest, context: { params: Promise<{ channelId: string }> }) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { channelId } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/channels/whatsapp/linked-device/${channelId}/reconnect`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
  });
  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}