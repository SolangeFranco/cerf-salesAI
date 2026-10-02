import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function GET(request: NextRequest) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const response = await fetch(`${getApiBaseUrl()}/api/orders`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}

export async function POST(request: NextRequest) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const apiResponse = await fetch(`${getApiBaseUrl()}/api/orders`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
      "Idempotency-Key": request.headers.get("Idempotency-Key") ?? "",
    },
    body: await request.text(),
  });

  return new NextResponse(await apiResponse.text(), {
    status: apiResponse.status,
    headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
  });
}
