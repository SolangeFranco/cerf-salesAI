import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ orderId: string }> };

export async function PATCH(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const { orderId } = await context.params;
  const apiResponse = await fetch(`${getApiBaseUrl()}/api/orders/${orderId}/status`, {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: await request.text(),
  });

  return new NextResponse(await apiResponse.text(), {
    status: apiResponse.status,
    headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
  });
}
