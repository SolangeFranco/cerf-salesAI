import { NextRequest, NextResponse } from "next/server";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export async function POST(request: NextRequest) {
  const body = await request.json();

  const apiResponse = await fetch(`${getApiBaseUrl()}/api/auth/register`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      tenantName: body.tenantName,
      email: body.email,
      password: body.password,
    }),
  });

  if (!apiResponse.ok) {
    const problem = await apiResponse.json().catch(() => null);
    return NextResponse.json(
      { error: extractErrorMessage(apiResponse.status, problem) },
      { status: apiResponse.status },
    );
  }

  const data = (await apiResponse.json()) as { accessToken: string };
  const response = NextResponse.json({ ok: true });
  setSessionCookie(response, data.accessToken);
  return response;
}

function extractErrorMessage(status: number, problem: unknown): string {
  if (status === 409) {
    return "Ya existe una tienda registrada con un nombre similar.";
  }
  if (problem && typeof problem === "object" && "errors" in problem) {
    const errors = (problem as { errors: Record<string, string[]> }).errors;
    return Object.values(errors).flat().join(" ");
  }
  return "No se pudo completar el registro. Intenta nuevamente.";
}

function setSessionCookie(response: NextResponse, token: string) {
  response.cookies.set(SESSION_COOKIE_NAME, token, {
    httpOnly: true,
    secure: process.env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: 60 * 60,
  });
}
