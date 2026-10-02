import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function GET(request: NextRequest) {
  return forward(request, "GET");
}

export async function POST(request: NextRequest) {
  return forward(request, "POST");
}

async function forward(request: NextRequest, method: "GET" | "POST") {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const apiResponse = await fetch(`${getApiBaseUrl()}/api/products`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(method === "POST" ? { "Content-Type": "application/json" } : {}),
    },
    body: method === "POST" ? await request.text() : undefined,
  });

  const responseBody = await apiResponse.text();
  return new NextResponse(responseBody, {
    status: apiResponse.status,
    headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
  });
}
