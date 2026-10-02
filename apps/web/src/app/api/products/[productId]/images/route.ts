import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ productId: string }> };

export async function POST(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });

  const { productId } = await context.params;
  const formData = await request.formData();
  const apiFormData = new FormData();
  for (const value of formData.getAll("files")) {
    if (value instanceof File) apiFormData.append("files", value, value.name);
  }

  const response = await fetch(`${getApiBaseUrl()}/api/products/${productId}/images`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: apiFormData,
  });

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}

export async function DELETE(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return NextResponse.json({ error: "No autenticado." }, { status: 401 });

  const { productId } = await context.params;
  const imageId = request.nextUrl.searchParams.get("imageId");
  if (!imageId) return NextResponse.json({ error: "La imagen es requerida." }, { status: 400 });

  const response = await fetch(`${getApiBaseUrl()}/api/products/${productId}/images/${imageId}`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${token}` },
  });

  return new NextResponse(await response.text(), {
    status: response.status,
    headers: { "Content-Type": response.headers.get("Content-Type") ?? "application/json" },
  });
}