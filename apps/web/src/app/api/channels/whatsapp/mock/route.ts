import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function POST(request: NextRequest) {
  if (process.env.NEXT_PUBLIC_ENABLE_STAGING_DEMO !== "true") {
    return NextResponse.json({ error: "Canal de prueba no disponible." }, { status: 404 });
  }

  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });

  const body = await request.text();
  const response = await fetch(`${getApiBaseUrl()}/api/channels/whatsapp/mock`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body,
  });

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}
