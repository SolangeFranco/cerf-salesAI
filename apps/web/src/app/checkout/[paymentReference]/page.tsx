"use client";

import { use, useEffect, useMemo, useState } from "react";
import { CheckCircle2, Download, LockKeyhole, MessageCircle, ReceiptText } from "lucide-react";
import { ErrorState } from "@/components/ui/error-state";
import { Skeleton } from "@/components/ui/skeleton";

type CheckoutItem = {
  productName: string;
  sku: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
};

type Checkout = {
  storeName: string;
  orderNumber: string;
  orderStatus: string;
  paymentStatus: string;
  total: number;
  items: CheckoutItem[];
  paymentProvider: string;
  bankName: string | null;
  beneficiaryName: string | null;
  accountNumber: string | null;
  documentNumber: string | null;
  alias: string | null;
};

const money = (value: number) => new Intl.NumberFormat("es-PY", { maximumFractionDigits: 0 }).format(value);

export default function CheckoutPage({ params }: { params: Promise<{ paymentReference: string }> }) {
  const { paymentReference } = use(params);
  const [checkout, setCheckout] = useState<Checkout | null>(null);
  const [error, setError] = useState<string | null>(null);

  const loadCheckout = () => {
    setError(null);
    setCheckout(null);
    void fetch(`/api/public/checkout/${encodeURIComponent(paymentReference)}`, { cache: "no-store" })
      .then(async (response) => {
        if (!response.ok) throw new Error("Este enlace de pago no está disponible o ya venció.");
        return response.json() as Promise<Checkout>;
      })
      .then(setCheckout)
      .catch((reason: Error) => setError(reason.message));
  };

  useEffect(() => { loadCheckout(); }, [paymentReference]);

  const receiptText = useMemo(() => {
    if (!checkout) return "";
    const items = checkout.items
      .map((item) => `- ${item.quantity} x ${item.productName} (${item.sku}) - Gs. ${money(item.lineTotal)}`)
      .join("\n");
    return [
      "RECIBO VANTA",
      `Tienda: ${checkout.storeName}`,
      `Pedido: ${checkout.orderNumber}`,
      `Estado del pedido: ${checkout.orderStatus}`,
      `Estado del pago: ${checkout.paymentStatus}`,
      "",
      "Productos:",
      items,
      "",
      `Total: Gs. ${money(checkout.total)}`,
      `Referencia de pago: ${paymentReference}`,
    ].join("\n");
  }, [checkout, paymentReference]);

  const downloadReceipt = () => {
    if (!checkout) return;
    const blob = new Blob([receiptText], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `recibo-${checkout.orderNumber}.txt`;
    link.click();
    URL.revokeObjectURL(url);
  };

  if (error) {
    return <main className="min-h-screen bg-[#f6f5f1] px-5 py-10 text-[#171b14] sm:py-20"><div className="mx-auto max-w-md pt-16"><ErrorState message={error} onRetry={loadCheckout} /></div></main>;
  }

  if (!checkout) {
    return <main className="min-h-screen bg-[#f6f5f1] px-5 py-10 text-[#171b14] sm:py-20"><div className="mx-auto grid max-w-5xl gap-8 lg:grid-cols-[1fr_390px]"><div className="space-y-5 pt-16"><Skeleton className="h-3 w-28 bg-[#dfe2d9]" /><Skeleton className="h-24 w-4/5 bg-[#dfe2d9]" /><Skeleton className="h-16 max-w-lg bg-[#dfe2d9]" /></div><div className="space-y-4 rounded-[26px] bg-white p-6"><Skeleton className="h-6 w-32" /><Skeleton className="h-28" /><Skeleton className="h-12" /></div></div></main>;
  }

  const isPaid = checkout.paymentStatus === "Paid";
  const isPaidAfterExpiration = checkout.paymentStatus === "PaidAfterExpiration";
  const isManualTransfer = checkout.paymentProvider === "manual-transfer";

  return (
    <main className="min-h-screen bg-[#f6f5f1] px-5 py-10 text-[#171b14] sm:py-20">
      <div className="mx-auto max-w-5xl">
        <header className="flex items-center gap-3">
          <span className="flex h-9 w-9 items-center justify-center rounded-full bg-[#11150e] font-black text-white">V</span>
          <div><div className="text-sm font-bold">{checkout.storeName}</div><div className="text-[10px] text-[#788073]">Checkout y recibo</div></div>
        </header>

        <div className="mt-12 grid gap-8 lg:grid-cols-[1fr_390px]">
          <section>
            <div className="text-[10px] font-bold uppercase tracking-[0.22em] text-[#75a832]">Pedido {checkout.orderNumber}</div>
            <h1 className="mt-4 text-5xl font-semibold leading-[0.9] tracking-[-0.07em]">
              {isPaid ? "Tu recibo está listo" : isPaidAfterExpiration ? "Pago recibido para revisión" : "Terminá tu compra"}<br />
              <span className="italic text-[#75a832]">desde el ecommerce.</span>
            </h1>
            <p className="mt-5 max-w-lg text-sm leading-relaxed text-[#697066]">
              {isPaid
                ? "El pago fue confirmado. Podés revisar el recibo en pantalla y descargar una copia."
                : isPaidAfterExpiration
                  ? "El pago llegó después de que la reserva venció. El comercio debe revisarlo antes de continuar."
                  : isManualTransfer
                    ? "Transferí el total indicado. El comercio revisará el comprobante y confirmará el pago manualmente."
                    : "Cuando el proveedor confirme el pago, esta pantalla mostrará el recibo final descargable."}
            </p>
            <div className="mt-8 flex items-center gap-3 rounded-2xl border border-[#dfe2d9] bg-white p-4 text-xs text-[#586052]">
              <LockKeyhole className="h-4 w-4 text-[#75a832]" />
              {isManualTransfer ? "El pedido seguirá pendiente hasta que el comercio confirme la transferencia." : "El pago solo se marca como confirmado cuando VANTA recibe la confirmación del proveedor."}
            </div>
            {isManualTransfer && <section className="mt-6 border-y border-[#dfe2d9] py-5" aria-label="Datos para transferencia">
              <h2 className="text-sm font-bold">Datos para transferir</h2>
              <dl className="mt-3 grid grid-cols-[130px_1fr] gap-x-4 gap-y-2 text-xs">
                {[["Banco", checkout.bankName], ["Beneficiario", checkout.beneficiaryName], ["Cuenta", checkout.accountNumber], ["CI / RUC", checkout.documentNumber], ["Alias", checkout.alias]].map(([label, value]) => value && <div key={label} className="contents"><dt className="text-[#788073]">{label}</dt><dd className="font-semibold">{value}</dd></div>)}
              </dl>
            </section>}
          </section>

          <aside className="rounded-[26px] border border-[#dfe2d9] bg-white p-6 shadow-[0_18px_42px_rgba(30,39,24,0.07)]">
            <div className="flex items-center justify-between gap-4">
              <div className="flex items-center gap-2 text-sm font-bold"><ReceiptText className="h-4 w-4 text-[#75a832]" /> Recibo</div>
              <span className={`rounded-full px-3 py-1 text-[10px] font-bold ${isPaid ? "bg-[#dff3d2] text-[#41632d]" : isPaidAfterExpiration ? "bg-[#f9e0b5] text-[#875b16]" : "bg-[#f1ead1] text-[#776226]"}`}>{isPaid ? "Pagado" : isPaidAfterExpiration ? "Pago en revisión" : "Pendiente"}</span>
            </div>

            <div className="mt-5 space-y-4">
              {checkout.items.map((item) => <div key={item.sku} className="flex justify-between gap-4 text-xs"><div><div className="font-semibold">{item.productName}</div><div className="mt-1 text-[#788073]">{item.quantity} x Gs. {money(item.unitPrice)}</div></div><div className="font-bold">Gs. {money(item.lineTotal)}</div></div>)}
            </div>
            <div className="my-5 border-t border-[#e5e7e1]" />
            <div className="flex justify-between text-lg font-black"><span>Total</span><span>Gs. {money(checkout.total)}</span></div>
            <div className="mt-5 rounded-2xl bg-[#f6f5f1] p-4 text-[11px] leading-relaxed text-[#586052]"><pre className="whitespace-pre-wrap font-sans">{receiptText}</pre></div>
            <button type="button" onClick={downloadReceipt} className="mt-5 inline-flex w-full items-center justify-center gap-2 rounded-full bg-[#11150e] px-4 py-3 text-xs font-bold text-white">
              <Download className="h-3.5 w-3.5" /> Descargar recibo
            </button>
            <a href="https://wa.me/" target="_blank" rel="noreferrer" className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-full border border-[#dfe2d9] px-4 py-3 text-xs font-bold"><MessageCircle className="h-3.5 w-3.5" /> Volver a WhatsApp</a>
            {isPaid && <div className="mt-4 flex items-center gap-2 text-[10px] font-bold text-[#41632d]"><CheckCircle2 className="h-4 w-4" /> {isManualTransfer ? "Transferencia confirmada por el comercio" : "Pago confirmado por proveedor"}</div>}
          </aside>
        </div>
      </div>
    </main>
  );
}