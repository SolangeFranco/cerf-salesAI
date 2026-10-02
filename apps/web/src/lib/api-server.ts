import { cookies } from "next/headers";
import { getApiBaseUrl, SESSION_COOKIE_NAME } from "@/lib/session";

export type Tenant = {
  id: string;
  name: string;
  slug: string;
  logoUrl: string | null;
  hasWhatsAppChannel: boolean;
  storeSetupCompleted: boolean;
};

export type DashboardSummary = {
  products: number;
  orders: number;
  conversations: number;
  totalRevenue: number;
  paidOrders: number;
  aiAttributedRevenue: number;
  aiCreatedOrders: number;
  aiAssistedOrders: number;
  abandonedCartRecoveredRevenue: number;
  abandonedCartRecoveredOrders: number;
  hasAttributionData: boolean;
};

// Server-only: forwards the httpOnly session cookie as a Bearer token to the API.
export async function getCurrentTenant(): Promise<Tenant | null> {
  const cookieStore = await cookies();
  const token = cookieStore.get(SESSION_COOKIE_NAME)?.value;
  if (!token) {
    return null;
  }

  try {
    const response = await fetch(`${getApiBaseUrl()}/api/tenants/me`, {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });

    if (!response.ok) return null;
    return (await response.json()) as Tenant;
  } catch {
    return null;
  }
}

export async function getDashboardSummary(): Promise<DashboardSummary | null> {
  const cookieStore = await cookies();
  const token = cookieStore.get(SESSION_COOKIE_NAME)?.value;
  if (!token) return null;
  try {
    const response = await fetch(`${getApiBaseUrl()}/api/tenants/summary`, {
      headers: { Authorization: `Bearer ${token}` },
      cache: "no-store",
    });
    return response.ok ? ((await response.json()) as DashboardSummary) : null;
  } catch {
    return null;
  }
}
