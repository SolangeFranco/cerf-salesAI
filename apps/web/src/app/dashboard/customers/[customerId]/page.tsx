"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ChevronLeft, Edit2, Trash2, Mail, Phone, MapPin, Users } from "lucide-react";

type Customer = {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  address: string | null;
  createdAt: string;
};

type Order = {
  id: string;
  number: string;
  status: string;
  total: number;
  createdAt: string;
  customerId: string | null;
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

const avatarPalette = ["#f6d1c5", "#c9e4ff", "#d6ecc9", "#e6d6ff", "#ffe1b3", "#c9f0e2"];
const avatarColor = (seed: string) => avatarPalette[seed.charCodeAt(0) % avatarPalette.length];
const initials = (name: string) =>
  name.split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]?.toUpperCase()).join("") || "?";

const pendingTabs = ["Wishlist", "Reseñas", "Programa de lealtad", "Tickets de soporte", "Insights", "Actividad"];

export default function CustomerDetailPage({ params }: { params: Promise<{ customerId: string }> }) {
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [orders, setOrders] = useState<Order[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    params
      .then(async ({ customerId }) => {
        const [customersResponse, ordersResponse] = await Promise.all([
          fetch("/api/customers", { cache: "no-store" }),
          fetch("/api/orders", { cache: "no-store" }),
        ]);
        if (!customersResponse.ok) throw new Error("No se pudo cargar el cliente.");
        const customers = (await customersResponse.json()) as Customer[];
        const found = customers.find((current) => current.id === customerId);
        if (!found) throw new Error("Cliente no encontrado.");
        setCustomer(found);
        setOrders(ordersResponse.ok ? ((await ordersResponse.json()) as Order[]).filter((order) => order.customerId === customerId) : []);
      })
      .catch((reason: Error) => setError(reason.message));
  }, [params]);

  if (error) {
    return (
      <div className="flex flex-col gap-4">
        <Link href="/dashboard/customers" className="text-sm text-muted-foreground hover:text-foreground">
          Volver a clientes
        </Link>
        <p className="text-sm text-destructive">{error}</p>
      </div>
    );
  }

  if (!customer) {
    return <p className="text-sm text-muted-foreground">Cargando cliente...</p>;
  }

  const totalSpent = orders.reduce((acc, order) => acc + order.total, 0);
  const avgOrderValue = orders.length > 0 ? totalSpent / orders.length : 0;
  const lastOrder = orders[0] ?? null;

  return (
    <div className="grid gap-6 xl:grid-cols-[1fr_320px]">
      <section className="overflow-hidden rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] p-4 shadow-[0_12px_34px_rgba(24,22,20,0.04)] sm:p-6 lg:p-7">
        <div className="flex items-center gap-4">
          <Link href="/dashboard/customers" aria-label="Volver a clientes" className="flex size-9 shrink-0 items-center justify-center rounded-full bg-[#f1efed] text-[#555c62] hover:text-[#24282c]">
            <ChevronLeft className="size-4" />
          </Link>
          <span className="flex size-14 shrink-0 items-center justify-center rounded-full text-[18px] font-semibold text-[#3c4247]" style={{ backgroundColor: avatarColor(customer.id) }}>
            {initials(customer.name)}
          </span>
          <div>
            <h1 className="text-[24px] font-semibold tracking-[-0.05em] text-[#1b1e21]">{customer.name}</h1>
            <p className="mt-1 text-[13px] text-[#8b9095]">
              Customer ID #{customer.id.slice(-6).toUpperCase()} · Cliente desde {new Date(customer.createdAt).toLocaleDateString("es-PY")}
            </p>
          </div>
        </div>

        <div className="mt-6 flex flex-wrap gap-2 border-b border-[#eee8e4] pb-3">
          <span className="rounded-full bg-[#1b1e21] px-4 py-2 text-[12px] font-semibold text-white">Historial de compras</span>
          {pendingTabs.map((tab) => (
            <span key={tab} title="Disponible próximamente" className="inline-flex items-center gap-1.5 rounded-full bg-[#f1efed] px-4 py-2 text-[12px] font-medium text-[#a4aaae]">
              {tab} <span className="rounded-full bg-[#e3e0dd] px-1.5 py-0.5 text-[9px] font-semibold text-[#8b9095]">Pronto</span>
            </span>
          ))}
        </div>

        <div className="mt-5 grid grid-cols-2 gap-4 rounded-[16px] border border-[#eee9e5] bg-[#faf9f8] p-4 sm:grid-cols-4">
          <div>
            <p className="text-[11px] text-[#9a9da0]">No. de pedidos</p>
            <p className="mt-1 text-[16px] font-semibold text-[#1b1e21]">{orders.length}</p>
          </div>
          <div>
            <p className="text-[11px] text-[#9a9da0]">Total gastado</p>
            <p className="mt-1 text-[16px] font-semibold text-[#1b1e21]">{currency.format(totalSpent)}</p>
          </div>
          <div>
            <p className="text-[11px] text-[#9a9da0]">Valor promedio</p>
            <p className="mt-1 text-[16px] font-semibold text-[#1b1e21]">{currency.format(avgOrderValue)}</p>
          </div>
          <div>
            <p className="text-[11px] text-[#9a9da0]">Último pedido</p>
            <p className="mt-1 text-[16px] font-semibold text-[#1b1e21]">{lastOrder ? new Date(lastOrder.createdAt).toLocaleDateString("es-PY") : "—"}</p>
          </div>
        </div>

        <div className="mt-5 overflow-x-auto">
          {orders.length === 0 ? (
            <p className="rounded-[16px] border border-dashed border-[#e5dfdc] bg-[#faf9f8] px-4 py-8 text-center text-[13px] text-[#9a9da0]">
              Este cliente todavía no tiene pedidos.
            </p>
          ) : (
            <table className="min-w-[560px] w-full border-separate border-spacing-0 text-left">
              <thead>
                <tr className="text-[12px] font-semibold text-[#33383c]">
                  <th className="px-4 py-3">Pedido</th>
                  <th className="px-4 py-3">Fecha</th>
                  <th className="px-4 py-3">Estado</th>
                  <th className="px-4 py-3 text-right">Total</th>
                </tr>
              </thead>
              <tbody>
                {orders.map((order, index) => (
                  <tr key={order.id} className={`text-[12px] text-[#3f4549] ${index % 2 === 0 ? "bg-[#f1f1f1]" : "bg-transparent"}`}>
                    <td className={`px-4 py-3 ${index === 0 ? "rounded-l-[14px]" : ""}`}>
                      <Link href={`/dashboard/orders/${order.id}`} className="font-medium text-[#252a2e] hover:text-[#6f39cf]">
                        {order.number}
                      </Link>
                    </td>
                    <td className="px-4 py-3">{new Date(order.createdAt).toLocaleDateString("es-PY")}</td>
                    <td className="px-4 py-3">
                      <span className="inline-flex rounded-full bg-[#e6e1ff] px-3 py-1 text-[11px] font-medium text-[#4a2f9e]">{statusLabels[order.status] ?? order.status}</span>
                    </td>
                    <td className={`px-4 py-3 text-right font-medium ${index === 0 ? "rounded-r-[14px]" : ""}`}>{currency.format(order.total)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </section>

      <aside className="flex flex-col gap-5">
        <section className="rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] p-5 shadow-[0_12px_34px_rgba(24,22,20,0.04)]">
          <h2 className="text-[15px] font-semibold text-[#1b1e21]">Información de contacto</h2>
          <div className="mt-4 flex flex-col gap-2">
            <span className="inline-flex items-center gap-2 rounded-[10px] border border-[#e7e0dc] bg-white px-3 py-2.5 text-[13px] text-[#3f4549]">
              <Phone className="size-3.5 text-[#9a9da0]" /> {customer.phone}
            </span>
            <span className="inline-flex items-center gap-2 rounded-[10px] border border-[#e7e0dc] bg-white px-3 py-2.5 text-[13px] text-[#3f4549]">
              <Mail className="size-3.5 text-[#9a9da0]" /> {customer.email || "Sin email registrado"}
            </span>
          </div>
        </section>

        <section className="rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] p-5 shadow-[0_12px_34px_rgba(24,22,20,0.04)]">
          <h2 className="text-[15px] font-semibold text-[#1b1e21]">Dirección</h2>
          <div className="mt-4 flex items-start gap-2 rounded-[10px] border border-[#e7e0dc] bg-white px-3 py-2.5 text-[13px] text-[#3f4549]">
            <MapPin className="mt-0.5 size-3.5 shrink-0 text-[#9a9da0]" />
            <span>{customer.address || "Este cliente no tiene una dirección registrada."}</span>
          </div>
        </section>

        <section className="rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] p-5 shadow-[0_12px_34px_rgba(24,22,20,0.04)]">
          <h2 className="text-[15px] font-semibold text-[#1b1e21]">Acciones</h2>
          <div className="mt-4 flex flex-col gap-2">
            <button type="button" disabled title="Editar clientes estará disponible próximamente." className="inline-flex items-center justify-center gap-2 rounded-full border border-[#d9d6d3] bg-white px-4 py-2.5 text-[13px] font-medium text-[#3e454b] opacity-50">
              <Edit2 className="size-4" /> Editar cliente
            </button>
            <button type="button" disabled title="Eliminar clientes estará disponible próximamente." className="inline-flex items-center justify-center gap-2 rounded-full border border-[#d9d6d3] bg-white px-4 py-2.5 text-[13px] font-medium text-[#a44343] opacity-50">
              <Trash2 className="size-4" /> Eliminar cliente
            </button>
          </div>
        </section>

        <div className="flex items-center gap-2 rounded-[16px] border border-dashed border-[#e5dfdc] bg-[#faf9f8] px-4 py-3 text-[12px] text-[#9a9da0]">
          <Users className="size-4 shrink-0" /> Wishlist, reseñas, programa de lealtad y soporte llegarán en próximas versiones.
        </div>
      </aside>
    </div>
  );
}
