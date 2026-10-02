"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowUpRight, CircleDollarSign, MessageSquareText, Package, ShoppingBag } from "lucide-react";

type DashboardSummary = {
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

type Order = {
  id: string;
  number: string;
  status: string;
  total: number;
  createdAt: string;
  items: { productName: string; quantity: number }[];
};

const currency = new Intl.NumberFormat("es-PY", { style: "currency", currency: "PYG", maximumFractionDigits: 0 });
const statusLabels: Record<string, string> = {
  New: "Nuevo",
  Confirmed: "Confirmado",
  AwaitingPayment: "Esperando pago",
  Paid: "Pagado",
  Preparing: "En preparación",
  Shipped: "Enviado",
  Delivered: "Entregado",
  Cancelled: "Cancelado",
  Refunded: "Reembolsado",
  PaymentFailed: "Pago rechazado",
};

export default function DashboardOverviewPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    const loadDashboard = async () => {
      try {
        const [summaryResponse, ordersResponse] = await Promise.all([
          fetch("/api/tenants/summary", { cache: "no-store" }),
          fetch("/api/orders", { cache: "no-store" }),
        ]);
        if (!summaryResponse.ok || !ordersResponse.ok) {
          throw new Error("No se pudo cargar el resumen de tu tienda.");
        }
        const [summaryData, orderData] = await Promise.all([
          summaryResponse.json() as Promise<DashboardSummary>,
          ordersResponse.json() as Promise<Order[]>,
        ]);
        if (!cancelled) {
          setSummary(summaryData);
          setOrders(orderData.slice(0, 5));
        }
      } catch (reason) {
        if (!cancelled) setError(reason instanceof Error ? reason.message : "No se pudo cargar el resumen de tu tienda.");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };

    void loadDashboard();
    return () => { cancelled = true; };
  }, []);

  const metrics = summary ? [
    { label: "Ingresos cobrados", value: currency.format(summary.totalRevenue), detail: `${summary.paidOrders} pedidos pagados`, icon: CircleDollarSign },
    { label: "Pedidos", value: summary.orders.toLocaleString("es-PY"), detail: "Pedidos registrados", icon: ShoppingBag },
    { label: "Conversaciones", value: summary.conversations.toLocaleString("es-PY"), detail: "Conversaciones de tu tienda", icon: MessageSquareText },
    { label: "Productos activos", value: summary.products.toLocaleString("es-PY"), detail: "Productos disponibles", icon: Package },
  ] : [];

  return (
    <main className="flex w-full flex-col gap-8">
      <header>
        <p className="text-sm font-medium text-muted-foreground">Dashboard</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Resumen de tu tienda</h1>
        <p className="mt-1 text-sm text-muted-foreground">Actividad basada en los datos registrados de tu tenant.</p>
      </header>

      {isLoading && <p role="status" className="text-sm text-muted-foreground">Cargando resumen...</p>}
      {error && <div role="alert" className="border-l-2 border-destructive pl-3 text-sm text-destructive">{error}</div>}

      {summary && !error && <>
        <section aria-label="Indicadores de la tienda" className="grid gap-5 border-y py-6 sm:grid-cols-2 xl:grid-cols-4">
          {metrics.map(({ label, value, detail, icon: Icon }) => (
            <article key={label} className="min-w-0">
              <div className="flex items-center gap-2 text-sm text-muted-foreground"><Icon aria-hidden="true" className="size-4" />{label}</div>
              <p className="mt-3 break-words text-2xl font-semibold">{value}</p>
              <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
            </article>
          ))}
        </section>

        <section aria-labelledby="ai-revenue-heading" className="border-b pb-6">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <h2 id="ai-revenue-heading" className="text-base font-semibold">Ingresos atribuidos a IA</h2>
            <span className="text-lg font-semibold">{summary.hasAttributionData ? currency.format(summary.aiAttributedRevenue) : "Sin datos"}</span>
          </div>
          <p className="mt-1 text-sm text-muted-foreground">
            {summary.hasAttributionData
              ? `${summary.aiCreatedOrders} pedidos creados y ${summary.aiAssistedOrders} asistidos por IA.`
              : "Todavía no hay pedidos pagados con atribución verificable."}
          </p>
        </section>

        <section aria-labelledby="recent-orders-heading">
          <div className="flex items-center justify-between gap-4 border-b pb-3">
            <div>
              <h2 id="recent-orders-heading" className="text-base font-semibold">Pedidos recientes</h2>
              <p className="mt-1 text-sm text-muted-foreground">Estado y total de pedidos de tu tienda.</p>
            </div>
            <Link href="/dashboard/orders" className="inline-flex items-center gap-1 text-sm font-medium text-primary hover:underline">
              Ver pedidos <ArrowUpRight aria-hidden="true" className="size-4" />
            </Link>
          </div>

          {orders.length === 0
            ? <p className="py-8 text-sm text-muted-foreground">Todavía no hay pedidos.</p>
            : <ul className="divide-y">
                {orders.map((order) => (
                  <li key={order.id}>
                    <Link href={`/dashboard/orders/${order.id}`} className="flex flex-wrap items-center justify-between gap-3 py-4 hover:bg-muted/30">
                      <div className="min-w-0">
                        <p className="font-medium">{order.number}</p>
                        <p className="mt-1 truncate text-sm text-muted-foreground">
                          {order.items.map((item) => `${item.quantity} × ${item.productName}`).join(", ")}
                        </p>
                      </div>
                      <div className="flex items-center gap-4 text-sm">
                        <span className="text-muted-foreground">{statusLabels[order.status] ?? order.status}</span>
                        <span className="font-medium">{currency.format(order.total)}</span>
                        <time dateTime={order.createdAt} className="hidden text-muted-foreground sm:inline">
                          {new Date(order.createdAt).toLocaleDateString("es-PY")}
                        </time>
                      </div>
                    </Link>
                  </li>
                ))}
              </ul>}
        </section>
      </>}
    </main>
  );
}
