// Server-only helpers. The API base URL and session cookie are never exposed to the browser;
// the Next.js route handlers act as a backend-for-frontend so the JWT stays in an httpOnly cookie.

export const SESSION_COOKIE_NAME = "vanta_session";

export function getApiBaseUrl(): string {
  return process.env.API_BASE_URL ?? "http://localhost:5052";
}
