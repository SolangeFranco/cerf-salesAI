import { NextRequest, NextResponse } from "next/server";
import { SESSION_COOKIE_NAME } from "@/lib/session";

export async function middleware(request: NextRequest) {
  const token = request.cookies.get(SESSION_COOKIE_NAME)?.value;

  if (!token) {
    const loginUrl = new URL("/login", request.url);
    loginUrl.searchParams.set("from", request.nextUrl.pathname);
    return NextResponse.redirect(loginUrl);
  }

  try {
    const response = await fetch(`${process.env.API_BASE_URL ?? "http://localhost:5052"}/api/tenants/me`, {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });
    if (response.ok) return NextResponse.next();
    if (response.status !== 401) return NextResponse.next();
  } catch {
    // The protected page will render its normal unavailable state if the API is down.
  }

  const loginUrl = new URL("/login", request.url);
  loginUrl.searchParams.set("from", request.nextUrl.pathname);
  const redirect = NextResponse.redirect(loginUrl);
  redirect.cookies.delete(SESSION_COOKIE_NAME);
  return redirect;
}

export const config = {
  matcher: ["/dashboard/:path*"],
};
