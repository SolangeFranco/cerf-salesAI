import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ productId: string }> };

export async function GET(request: NextRequest, context: RouteContext) {
  return forward(request, context, "GET");
}

export async function POST(request: NextRequest, context: RouteContext) {
  return forward(request, context, "POST");
}

export async function PATCH(request: NextRequest, context: RouteContext) {
  return forward(request, context, "PATCH");
}

async function forward(request: NextRequest, context: RouteContext, method: "GET" | "POST" | "PATCH") {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const { productId } = await context.params;
  const apiResponse = await fetch(`${getApiBaseUrl()}/api/products/${productId}/variants`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(method === "POST" ? { "Content-Type": "application/json" } : {}),
    },
    body: method === "POST" ? await request.text() : undefined,
  });

  return new NextResponse(await apiResponse.text(), {
    status: apiResponse.status,
    headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
  });
}
