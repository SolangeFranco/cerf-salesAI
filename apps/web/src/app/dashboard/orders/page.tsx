"use client";

import Link from "next/link";
import { Check, Download, Search } from "lucide-react";
import { useEffect, useMemo, useState } from "react";

type OrderSummary = {
  id: string;
  number: string;
  status: string;
  total: number;
  createdAt: string;
  customerId: string | null;
  items: { productName: string; sku: string; quantity: number; unitPrice: number; lineTotal: number }[];
};

type DashboardSummary = { totalRevenue: number; paidOrders: number };

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

const currency = new Intl.NumberFormat("es-PY", { style: "currency", currency: "PYG", maximumFractionDigits: 0 });
const shortDate = new Intl.DateTimeFormat("es-PY", { day: "numeric", month: "short", year: "numeric" });

export default function OrdersPage() {
  const [orders, setOrders] = useState<OrderSummary[]>([]);
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [search, setSearch] = useState("");
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    const loadOrders = async () => {
      try {
        const [ordersResponse, summaryResponse] = await Promise.all([
          fetch("/api/orders", { cache: "no-store" }),
          fetch("/api/tenants/summary", { cache: "no-store" }),
        ]);
        if (!ordersResponse.ok || !summaryResponse.ok) throw new Error("No se pudieron cargar los pedidos.");
        const [orderData, summaryData] = await Promise.all([
          ordersResponse.json() as Promise<OrderSummary[]>,
          summaryResponse.json() as Promise<DashboardSummary>,
        ]);
        if (!cancelled) {
          setOrders(orderData);
          setSummary(summaryData);
        }
      } catch (reason) {
        if (!cancelled) setError(reason instanceof Error ? reason.message : "No se pudieron cargar los pedidos.");
      } finally {
        if (!cancelled) setIsLoading(false);
      }
    };
    void loadOrders();
    return () => { cancelled = true; };
  }, []);

  const filteredOrders = useMemo(() => {
    const query = search.trim().toLowerCase();
    if (!query) return orders;
    return orders.filter((order) => `${order.number} ${statusLabels[order.status] ?? order.status} ${order.items.map((item) => item.productName).join(" ")}`.toLowerCase().includes(query));
  }, [orders, search]);
  const pendingOrders = orders.filter((order) => ["New", "Confirmed", "AwaitingPayment", "PaymentFailed"].includes(order.status));
  const allSelected = filteredOrders.length > 0 && filteredOrders.every((order) => selectedIds.includes(order.id));

  const toggleAll = () => setSelectedIds(allSelected ? [] : filteredOrders.map((order) => order.id));
  const toggleOrder = (id: string) => setSelectedIds((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]);
  const exportSelected = () => {
    const selected = orders.filter((order) => selectedIds.includes(order.id));
    const rows = [
      ["Número", "Estado", "Total PYG", "Fecha"],
      ...selected.map((order) => [order.number, statusLabels[order.status] ?? order.status, String(order.total), order.createdAt]),
    ];
    const csv = rows.map((row) => row.map((value) => `"${value.replaceAll('"', '""')}"`).join(",")).join("\n");
    const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = "pedidos-vanta.csv";
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <main className="flex w-full flex-col gap-6">
      <header>
        <p className="text-sm font-medium text-muted-foreground">Comercio</p>
        <h1 className="mt-1 text-2xl font-semibold tracking-tight">Pedidos</h1>
        <p className="mt-1 text-sm text-muted-foreground">Pedidos y pagos registrados para tu tienda.</p>
      </header>

      <section aria-label="Resumen de pedidos" className="grid gap-5 border-y py-5 sm:grid-cols-3">
        <article><p className="text-sm text-muted-foreground">Ingresos cobrados</p><p className="mt-2 text-xl font-semibold">{summary ? currency.format(summary.totalRevenue) : "—"}</p><p className="mt-1 text-xs text-muted-foreground">{summary ? `${summary.paidOrders} pedidos pagados` : "Cargando datos"}</p></article>
        <article><p className="text-sm text-muted-foreground">Pedidos</p><p className="mt-2 text-xl font-semibold">{orders.length.toLocaleString("es-PY")}</p><p className="mt-1 text-xs text-muted-foreground">Registrados en tu tenant</p></article>
        <article><p className="text-sm text-muted-foreground">Pendientes de pago/gestión</p><p className="mt-2 text-xl font-semibold">{pendingOrders.length.toLocaleString("es-PY")}</p><p className="mt-1 text-xs text-muted-foreground">Estados previos a la preparación</p></article>
      </section>

      <section aria-label="Listado de pedidos" className="overflow-hidden border-b">
        <div className="flex flex-wrap items-center justify-between gap-4 border-b py-4">
          <label className="flex min-w-0 items-center gap-2 rounded-md border px-3 py-2 text-muted-foreground sm:w-80">
            <Search aria-hidden="true" className="size-4 shrink-0" />
            <input aria-label="Buscar pedidos" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Buscar por número, producto o estado" className="min-w-0 flex-1 bg-transparent text-sm text-foreground outline-none placeholder:text-muted-foreground" />
          </label>
          <span className="text-sm text-muted-foreground">{filteredOrders.length} pedidos</span>
        </div>

        {isLoading && <p role="status" className="py-8 text-sm text-muted-foreground">Cargando pedidos...</p>}
        {error && <p role="alert" className="py-8 text-sm text-destructive">{error}</p>}
        {!isLoading && !error && filteredOrders.length === 0 && <p className="py-12 text-center text-sm text-muted-foreground">{search ? "No hay pedidos que coincidan con la búsqueda." : "Aún no hay pedidos."}</p>}

        {!isLoading && !error && filteredOrders.length > 0 && <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead><tr className="border-b text-xs text-muted-foreground">
              <th className="w-12 py-3"><button type="button" aria-label="Seleccionar todos" onClick={toggleAll} className="flex size-4 items-center justify-center rounded border">{allSelected && <Check aria-hidden="true" className="size-3" />}</button></th>
              <th className="py-3">Pedido</th><th className="py-3">Artículos</th><th className="py-3">Total</th><th className="py-3">Estado</th><th className="py-3">Fecha</th><th className="py-3">Detalle</th>
            </tr></thead>
            <tbody className="divide-y">
              {filteredOrders.map((order) => <tr key={order.id}>
                <td className="py-4"><button type="button" aria-label={`Seleccionar pedido ${order.number}`} onClick={() => toggleOrder(order.id)} className="flex size-4 items-center justify-center rounded border">{selectedIds.includes(order.id) && <Check aria-hidden="true" className="size-3" />}</button></td>
                <td className="py-4 font-medium">{order.number}</td>
                <td className="py-4 text-muted-foreground">{order.items.map((item) => `${item.quantity} × ${item.productName}`).join(", ")}</td>
                <td className="py-4 font-medium">{currency.format(order.total)}</td>
                <td className="py-4">{statusLabels[order.status] ?? order.status}</td>
                <td className="py-4 text-muted-foreground"><time dateTime={order.createdAt}>{shortDate.format(new Date(order.createdAt))}</time></td>
                <td className="py-4"><Link href={`/dashboard/orders/${order.id}`} className="font-medium text-primary hover:underline">Ver pedido</Link></td>
              </tr>)}
            </tbody>
          </table>
        </div>}
      </section>

      {selectedIds.length > 0 && <div className="fixed bottom-6 left-1/2 z-20 flex -translate-x-1/2 items-center gap-3 border bg-background px-4 py-3 shadow-lg">
        <span className="text-sm">{selectedIds.length} seleccionados</span>
        <button type="button" onClick={exportSelected} className="inline-flex items-center gap-2 text-sm font-medium"><Download aria-hidden="true" className="size-4" />Exportar CSV</button>
        <button type="button" onClick={() => setSelectedIds([])} aria-label="Limpiar selección" className="px-2 text-lg">×</button>
      </div>}
    </main>
  );
}
