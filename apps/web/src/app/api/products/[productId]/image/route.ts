import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

type RouteContext = { params: Promise<{ productId: string }> };

export async function POST(request: NextRequest, context: RouteContext) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return NextResponse.json({ error: "No autenticado." }, { status: 401 });
  }

  const { productId } = await context.params;
  const formData = await request.formData();
  const file = formData.get("file");
  if (!(file instanceof File)) {
    return NextResponse.json({ error: "El archivo de imagen es requerido." }, { status: 400 });
  }

  const apiFormData = new FormData();
  apiFormData.append("file", file, file.name);

  try {
    const apiResponse = await fetch(`${getApiBaseUrl()}/api/products/${productId}/image`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: apiFormData,
    });

    return new NextResponse(await apiResponse.text(), {
      status: apiResponse.status,
      headers: { "Content-Type": apiResponse.headers.get("Content-Type") ?? "application/json" },
    });
  } catch {
    return NextResponse.json({ error: "No se pudo conectar con el servidor." }, { status: 503 });
  }
}
