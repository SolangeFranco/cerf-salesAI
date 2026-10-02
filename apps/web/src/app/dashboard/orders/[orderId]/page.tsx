"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { cn } from "@/lib/utils";

type Order = {
  id: string;
  number: string;
  status: string;
  total: number;
  createdAt: string;
  conversationId: string | null;
  customer: { name: string; phone: string; email: string | null; address: string | null } | null;
  payment: { id: string; status: string; amount: number; provider: string; bankName?: string; beneficiaryName?: string; accountNumber?: string; documentNumber?: string; alias?: string } | null;
  statusHistory: { fromStatus: string; toStatus: string; createdAt: string }[];
  items: { productName: string; sku: string; quantity: number; unitPrice: number; lineTotal: number }[];
};

const currency = new Intl.NumberFormat("es-PY", { style: "currency", currency: "PYG", maximumFractionDigits: 0 });
const statusLabels: Record<string, string> = { New: "Nuevo", Confirmed: "Confirmado", AwaitingPayment: "Esperando pago", Paid: "Pagado", Preparing: "En preparación", Shipped: "Enviado", Delivered: "Entregado", Cancelled: "Cancelado", Refunded: "Reembolsado", PaymentFailed: "Pago rechazado", Pending: "Pendiente", PaidAfterExpiration: "Pago en revisión" };

export default function OrderDetailPage({ params }: { params: Promise<{ orderId: string }> }) {
  const [order, setOrder] = useState<Order | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isPaymentLoading, setIsPaymentLoading] = useState(false);

  useEffect(() => {
    params.then(({ orderId }) => fetch(`/api/orders/${orderId}`, { cache: "no-store" }))
      .then(async (response) => {
        if (!response.ok) throw new Error(response.status === 404 ? "Pedido no encontrado." : "No se pudo cargar el pedido.");
        setOrder((await response.json()) as Order);
      })
      .catch((reason: Error) => setError(reason.message));
  }, [params]);

  if (error) return <p className="text-sm text-destructive">{error}</p>;
  if (!order) return <p className="text-sm text-muted-foreground">Cargando pedido...</p>;

  const createPayment = async () => {
    setIsPaymentLoading(true);
    setError(null);
    const response = await fetch(`/api/orders/${order.id}/payments`, { method: "POST", headers: { "Idempotency-Key": `web-payment-${order.id}` } });
    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.error ?? "No se pudo solicitar el pago.");
    } else {
      const payment = await response.json();
      setOrder({ ...order, payment });
    }
    setIsPaymentLoading(false);
  };

  const confirmMockPayment = async () => {
    if (!order.payment) return;
    setIsPaymentLoading(true);
    const response = await fetch(`/api/payments/${order.payment.id}/mock-confirm`, { method: "POST" });
    if (response.ok) setOrder({ ...order, payment: await response.json() });
    setIsPaymentLoading(false);
  };

  const requestManualTransfer = async () => {
    setIsPaymentLoading(true);
    setError(null);
    const response = await fetch(`/api/orders/${order.id}/payments`, { method: "PUT", headers: { "Idempotency-Key": `manual-transfer-${order.id}` } });
    const data = await response.json().catch(() => null);
    if (!response.ok) setError(data?.error ?? "No se pudo solicitar la transferencia.");
    else setOrder({ ...order, payment: data.payment ? { ...data.payment, bankName: data.bankName, beneficiaryName: data.beneficiaryName, accountNumber: data.accountNumber, documentNumber: data.documentNumber, alias: data.alias } : data });
    setIsPaymentLoading(false);
  };

  const confirmManualTransfer = async () => {
    if (!order.payment) return;
    setIsPaymentLoading(true);
    const response = await fetch(`/api/payments/${order.payment.id}/manual-confirm`, { method: "POST" });
    const data = await response.json().catch(() => null);
    if (response.ok) setOrder({ ...order, payment: data });
    else setError(data?.error ?? "No se pudo marcar la transferencia como pagada.");
    setIsPaymentLoading(false);
  };

  return (
    <div className="flex flex-col gap-8">
      <div className="flex items-start justify-between gap-4">
        <div>
          <Link href="/dashboard/orders" className="text-sm text-muted-foreground hover:text-foreground">Volver a pedidos</Link>
          <div className="mt-2 flex items-center gap-3"><h1 className="text-2xl font-semibold tracking-tight">{order.number}</h1><Badge variant="secondary">{statusLabels[order.status] ?? order.status}</Badge></div>
          <p className="text-sm text-muted-foreground">Creado el {new Date(order.createdAt).toLocaleString("es-PY")}</p>
        </div>
        <Link href="/dashboard/orders" className={cn(buttonVariants({ variant: "outline" }))}>Cerrar detalle</Link>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_320px]">
        <Card>
          <CardHeader><CardTitle className="text-lg">Productos</CardTitle><CardDescription>{order.items.length} líneas en este pedido.</CardDescription></CardHeader>
          <CardContent className="divide-y">
            {order.items.map((item) => (
              <div key={`${item.sku}-${item.productName}`} className="flex items-center justify-between gap-4 py-4">
                <div><p className="font-medium">{item.quantity} × {item.productName}</p><p className="text-sm text-muted-foreground">{item.sku} · {currency.format(item.unitPrice)} c/u</p></div>
                <p className="font-medium">{currency.format(item.lineTotal)}</p>
              </div>
            ))}
            <div className="flex justify-between pt-4 text-lg font-semibold"><span>Total</span><span>{currency.format(order.total)}</span></div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Pago</CardTitle><CardDescription>{order.payment?.provider === "manual-transfer" ? "Transferencia bancaria manual." : "Pago pendiente de configuración."}</CardDescription></CardHeader>
          <CardContent className="flex flex-col gap-3">
            {order.payment ? <><div className="flex justify-between text-sm"><span>Estado</span><Badge variant="secondary">{statusLabels[order.payment.status] ?? order.payment.status}</Badge></div><p className="text-sm text-muted-foreground">{currency.format(order.payment.amount)}</p>{order.payment.provider === "manual-transfer" && order.payment.bankName && <div className="rounded-lg bg-muted/50 p-3 text-xs"><p>Banco: {order.payment.bankName}</p><p>Beneficiario: {order.payment.beneficiaryName}</p><p>Cuenta: {order.payment.accountNumber}</p><p>Documento: {order.payment.documentNumber}</p><p>Alias: {order.payment.alias || "No informado"}</p></div>}{order.payment.provider === "manual-transfer" && order.payment.status === "Pending" && <button type="button" disabled={isPaymentLoading} onClick={() => void confirmManualTransfer()} className="inline-flex h-9 items-center justify-center rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/80 disabled:opacity-50">Marcar transferencia como pagada</button>}</> : <button type="button" disabled={isPaymentLoading || (order.status !== "Confirmed" && order.status !== "PaymentFailed")} onClick={() => void requestManualTransfer()} className="inline-flex h-9 items-center justify-center rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/80 disabled:opacity-50">{order.status === "Confirmed" || order.status === "PaymentFailed" ? "Solicitar transferencia" : "Confirmá el pedido primero"}</button>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Historial</CardTitle><CardDescription>Seguimiento de estados del pedido.</CardDescription></CardHeader>
          <CardContent>
            <div className="flex flex-col gap-4">
              {order.statusHistory.map((event) => (
                <div key={`${event.toStatus}-${event.createdAt}`} className="flex items-start gap-3 text-sm">
                  <span className="mt-1 size-2 shrink-0 rounded-full bg-primary" />
                  <div><p className="font-medium">{event.fromStatus ? `${statusLabels[event.fromStatus] ?? event.fromStatus} → ` : "Pedido creado → "}{statusLabels[event.toStatus] ?? event.toStatus}</p><p className="text-muted-foreground">{new Date(event.createdAt).toLocaleString("es-PY")}</p></div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle className="text-lg">Cliente</CardTitle><CardDescription>Contexto asociado al pedido.</CardDescription></CardHeader>
          <CardContent>
            {order.customer ? <div className="flex flex-col gap-2 text-sm"><p className="font-medium">{order.customer.name}</p><p className="text-muted-foreground">{order.customer.phone}</p>{order.customer.email && <p className="text-muted-foreground">{order.customer.email}</p>}{order.customer.address && <p className="text-muted-foreground">{order.customer.address}</p>}</div> : <p className="text-sm text-muted-foreground">Pedido sin cliente asociado.</p>}
          </CardContent>
        </Card>

        {order.conversationId && <Card>
          <CardHeader><CardTitle className="text-lg">Conversación de origen</CardTitle><CardDescription>Contexto comercial que originó este pedido.</CardDescription></CardHeader>
          <CardContent><Link href={`/dashboard/conversations/${order.conversationId}`} className="text-sm font-medium text-primary hover:underline">Abrir conversación</Link></CardContent>
        </Card>}
      </div>
    </div>
  );
}
