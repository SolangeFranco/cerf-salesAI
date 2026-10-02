import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ productId: string }> };

export async function GET(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const { productId } = await context.params;

  try {
    const apiResponse = await fetch(`${getApiBaseUrl()}/api/products/${productId}`, {
      method: "GET",
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });

    return new NextResponse(await apiResponse.text(), {
      status: apiResponse.status,
      headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
    });
  } catch {
    return NextResponse.json({ error: "No se pudo conectar con el servidor." }, { status: 503 });
  }
}

export async function PUT(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const { productId } = await context.params;

  try {
    const apiResponse = await fetch(`${getApiBaseUrl()}/api/products/${productId}`, {
      method: "PUT",
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
  } catch {
    return NextResponse.json({ error: "No se pudo conectar con el servidor." }, { status: 503 });
  }
}

export async function PATCH(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  const { productId } = await context.params;
  const apiResponse = await fetch(`${getApiBaseUrl()}/api/products/${productId}/stock`, {
    method: "PUT",
    headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    body: await request.text(),
  });
  return new NextResponse(await apiResponse.text(), {
    status: apiResponse.status,
    headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
  });
}
