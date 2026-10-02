import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ orderId: string }> };

export async function POST(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { orderId } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/orders/${orderId}/payments`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Idempotency-Key": request.headers.get("Idempotency-Key") ?? "",
    },
  });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/json" } });
}

export async function PUT(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { orderId } = await context.params;
  const response = await fetch(`${getApiBaseUrl()}/api/orders/${orderId}/payments/manual-transfer`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}`, "Idempotency-Key": request.headers.get("Idempotency-Key") ?? "" },
  });
  return new NextResponse(await response.text(), { status: response.status, headers: { "Content-Type": "application/json" } });
}
