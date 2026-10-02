import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function POST(request: NextRequest) {
  const body = await request.json();

  let apiResponse: Response;
  try {
    apiResponse = await fetch(`${getApiBaseUrl()}/api/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: body.email, password: body.password }),
    });
  } catch {
    return NextResponse.json(
      { error: "No se pudo conectar con el servidor. Verificá que el API esté ejecutándose." },
      { status: 503 },
    );
  }

  if (!apiResponse.ok) {
    return NextResponse.json(
      { error: "Email o contraseña incorrectos." },
      { status: apiResponse.status },
    );
  }

  const data = (await apiResponse.json()) as { accessToken: string };
  const response = NextResponse.json({ ok: true });
  response.cookies.set(SESSION_COOKIE_NAME, data.accessToken, {
    httpOnly: true,
    secure: request.nextUrl.protocol === "https:",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 60,
  });
  return response;
}
