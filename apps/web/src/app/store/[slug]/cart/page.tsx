"use client";

import Link from "next/link";
import { use, useEffect, useState } from "react";
import { ArrowLeft, ArrowRight, Minus, Plus, ShoppingBag, Trash2 } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";

type Variant = { id: string; name: string; sku: string; stockQuantity: number; priceOverride: number | null };
type Product = { id: string; name: string; description: string | null; sku: string; price: number; imageBase64: string | null; imageMediaType: string | null; variants: Variant[] };
type CartLine = { product: Product; variantId?: string; quantity: number };

const money = (value: number) => new Intl.NumberFormat("es-PY", { maximumFractionDigits: 0 }).format(value);
const cartKey = (slug: string) => `vanta-store-cart:${slug}`;
const linePrice = (line: CartLine) => line.product.variants.find((variant) => variant.id === line.variantId)?.priceOverride ?? line.product.price;
const lineLabel = (line: CartLine) => line.product.variants.find((variant) => variant.id === line.variantId)?.name ?? line.product.sku;

export default function StoreCartPage({ params }: { params: Promise<{ slug: string }> }) {
  const { slug } = use(params);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [cartLoaded, setCartLoaded] = useState(false);
  const [checkout, setCheckout] = useState({ name: "", phone: "", email: "", address: "" });
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);

  useEffect(() => {
    const stored = window.localStorage.getItem(cartKey(slug));
    if (stored) setCart(JSON.parse(stored) as CartLine[]);
    setCartLoaded(true);
  }, [slug]);

  useEffect(() => {
    if (!cartLoaded) return;
    window.localStorage.setItem(cartKey(slug), JSON.stringify(cart));
  }, [cart, slug, cartLoaded]);

  const total = cart.reduce((sum, line) => sum + linePrice(line) * line.quantity, 0);
  const updateQuantity = (index: number, quantity: number) => setCart((current) => current.map((line, lineIndex) => lineIndex === index ? { ...line, quantity: Math.max(1, Math.min(100, quantity)) } : line));
  const removeLine = (index: number) => setCart((current) => current.filter((_, lineIndex) => lineIndex !== index));

  const submitOrder = async () => {
    if (cart.length === 0) return;
    if (!checkout.name.trim() || !checkout.phone.trim() || !checkout.address.trim()) {
      setSubmitError("Completá nombre, teléfono y dirección para continuar.");
      return;
    }
    setSubmitting(true);
    setSubmitError(null);
    const response = await fetch(`/api/public/store/${encodeURIComponent(slug)}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        ...checkout,
        items: cart.map((line) => ({ productId: line.product.id, variantId: line.variantId ?? null, quantity: line.quantity })),
      }),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      setSubmitError(body?.error ?? "No pudimos crear el pedido.");
      setSubmitting(false);
      return;
    }
    const result = await response.json() as { checkoutUrl: string };
    window.localStorage.removeItem(cartKey(slug));
    window.location.assign(result.checkoutUrl);
  };

  return (
    <main className="min-h-screen bg-[#f6f5f1] px-5 py-8 text-[#171b14] sm:px-8">
      <div className="mx-auto max-w-6xl">
        <header className="flex items-center justify-between gap-4">
          <Link href={`/store/${slug}`} className="inline-flex items-center gap-2 rounded-full border border-[#dfe2d9] bg-white px-4 py-2 text-xs font-semibold"><ArrowLeft className="h-3.5 w-3.5" /> Volver al catálogo</Link>
          <div className="inline-flex items-center gap-2 rounded-full bg-[#11150e] px-4 py-2 text-xs font-bold text-white"><ShoppingBag className="h-3.5 w-3.5" /> {cart.reduce((sum, line) => sum + line.quantity, 0)} producto(s)</div>
        </header>

        <section className="grid gap-8 pt-12 lg:grid-cols-[1fr_390px]">
          <div>
            <div className="text-[10px] font-bold uppercase tracking-[0.22em] text-[#75a832]">Carrito de compra</div>
            <h1 className="mt-4 text-5xl font-semibold leading-[0.9] tracking-[-0.07em] sm:text-7xl">Revisá tu pedido<br /><span className="italic text-[#75a832]">antes de pagar.</span></h1>
            <div className="mt-10 space-y-4">
              {!cartLoaded ? <div className="space-y-4"><Skeleton className="h-28 bg-white" /><Skeleton className="h-28 bg-white" /></div> : cart.length === 0 ? <div className="rounded-[24px] border border-[#dfe2d9] bg-white p-8"><EmptyState icon={ShoppingBag} title="Tu carrito está vacío" description="Explorá el catálogo y agregá productos para comenzar." action={<Link href={`/store/${slug}`} className="inline-flex min-h-11 items-center rounded-full bg-[#11150e] px-5 py-3 text-xs font-bold text-white">Volver al catálogo</Link>} className="border-0 bg-transparent" /></div> : cart.map((line, index) => (
                <article key={`${line.product.id}-${line.variantId ?? "base"}`} className="grid gap-4 rounded-[24px] border border-[#dfe2d9] bg-white p-4 shadow-[0_16px_38px_rgba(30,39,24,0.05)] sm:grid-cols-[1fr_auto] sm:items-center">
                  <div><h2 className="font-bold">{line.product.name}</h2><p className="mt-1 text-xs text-[#697066]">{lineLabel(line)} · ₲ {money(linePrice(line))}</p></div>
                  <div className="flex items-center justify-between gap-3 sm:justify-end">
                    <div className="flex items-center rounded-full border border-[#dfe2d9] bg-[#f6f5f1] p-1"><button type="button" aria-label={`Reducir cantidad de ${line.product.name}`} onClick={() => updateQuantity(index, line.quantity - 1)} className="flex size-11 items-center justify-center rounded-full bg-white"><Minus className="h-3.5 w-3.5" /></button><span className="w-10 text-center text-sm font-bold">{line.quantity}</span><button type="button" aria-label={`Aumentar cantidad de ${line.product.name}`} onClick={() => updateQuantity(index, line.quantity + 1)} className="flex size-11 items-center justify-center rounded-full bg-white"><Plus className="h-3.5 w-3.5" /></button></div>
                    <div className="w-28 text-right text-sm font-black">₲ {money(linePrice(line) * line.quantity)}</div>
                    <button type="button" onClick={() => removeLine(index)} className="flex h-9 w-9 items-center justify-center rounded-full bg-[#fee2e2] text-[#991b1b]" aria-label={`Eliminar ${line.product.name}`}><Trash2 className="h-4 w-4" /></button>
                  </div>
                </article>
              ))}
            </div>
          </div>

          <aside className="h-fit rounded-[28px] border border-[#dfe2d9] bg-white p-6 shadow-[0_18px_42px_rgba(30,39,24,0.07)]">
            <h2 className="text-xl font-semibold">Datos de entrega</h2>
            <div className="mt-5 grid gap-3">
              {(["name", "phone", "email", "address"] as const).map((field) => <label key={field} className="grid gap-1 text-[11px] font-semibold text-[#586052]"><span>{({ name: "Nombre completo", phone: "Teléfono", email: "Email (opcional)", address: "Dirección de entrega" }[field])}</span><input required={field !== "email"} type={field === "email" ? "email" : "text"} autoComplete={field === "name" ? "name" : field === "phone" ? "tel" : field === "email" ? "email" : "street-address"} value={checkout[field]} onChange={(event) => setCheckout((current) => ({ ...current, [field]: event.target.value }))} placeholder={{ name: "Nombre completo", phone: "Teléfono", email: "Email (opcional)", address: "Dirección de entrega" }[field]} className="rounded-xl border border-[#dfe2d9] bg-[#f6f5f1] px-4 py-3 text-sm outline-none focus:ring-2 focus:ring-[#8bcf32]" /></label>)}
            </div>
            <div className="my-6 border-t border-[#e5e7e1]" />
            <div className="flex justify-between text-lg font-black"><span>Total</span><span>₲ {money(total)}</span></div>
            {submitError && <p role="alert" className="mt-3 rounded-xl bg-[#fee2e2] p-3 text-xs text-[#991b1b]">{submitError}</p>}
            <button type="button" disabled={cart.length === 0 || submitting} onClick={() => void submitOrder()} className="mt-6 inline-flex w-full items-center justify-center gap-2 rounded-full bg-[#11150e] px-5 py-3 text-xs font-bold text-white disabled:cursor-not-allowed disabled:opacity-50">{submitting ? "Creando pedido..." : "Continuar al pago"}<ArrowRight className="h-3.5 w-3.5" /></button>
          </aside>
        </section>
      </div>
    </main>
  );
}