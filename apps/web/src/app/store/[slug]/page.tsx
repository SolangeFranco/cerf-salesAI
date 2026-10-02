"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Check, MessageCircle, ShoppingBag } from "lucide-react";

type Variant = { id: string; name: string; sku: string; stockQuantity: number; priceOverride: number | null };
type Product = { id: string; name: string; description: string | null; sku: string; price: number; imageBase64: string | null; imageMediaType: string | null; variants: Variant[] };
type Store = { storeName: string; slug: string; logoUrl: string | null; products: Product[] };
type CartLine = { product: Product; variantId?: string; quantity: number };

const money = (value: number) => new Intl.NumberFormat("es-PY", { maximumFractionDigits: 0 }).format(value);
const imageUrl = (product: Product) => product.imageBase64 && product.imageMediaType ? `data:${product.imageMediaType};base64,${product.imageBase64}` : null;
const cartKey = (slug: string) => `vanta-store-cart:${slug}`;

export default function PublicStorePage({ params }: { params: Promise<{ slug: string }> }) {
  const [store, setStore] = useState<Store | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedVariant, setSelectedVariant] = useState<Record<string, string>>({});
  const [cart, setCart] = useState<CartLine[]>([]);
  const [addedProductId, setAddedProductId] = useState<string | null>(null);
  const [cartMessage, setCartMessage] = useState<Record<string, string>>({});

  useEffect(() => {
    params.then(({ slug }) => fetch(`/api/public/store/${encodeURIComponent(slug)}`, { cache: "no-store" }))
      .then(async (response) => {
        if (!response.ok) throw new Error("No pudimos encontrar esta tienda.");
        return response.json() as Promise<Store>;
      })
      .then((storeResponse) => {
        setStore(storeResponse);
        setSelectedVariant(Object.fromEntries(
          storeResponse.products
            .map((product) => [product.id, product.variants.find((variant) => variant.stockQuantity > 0)?.id])
            .filter(([, variantId]) => variantId),
        ));
        const stored = window.localStorage.getItem(cartKey(storeResponse.slug));
        if (stored) setCart(JSON.parse(stored) as CartLine[]);
      })
      .catch((reason: Error) => setError(reason.message));
  }, [params]);

  const addToCart = (product: Product) => {
    const variantId = selectedVariant[product.id] || undefined;
    if (product.variants.length > 0 && !variantId) {
      setCartMessage((current) => ({ ...current, [product.id]: "Elegí una variante disponible antes de agregar." }));
      return;
    }
    setCart((current) => {
      const existingIndex = current.findIndex((line) => line.product.id === product.id && line.variantId === variantId);
      const next = existingIndex >= 0
        ? current.map((line, index) => index === existingIndex ? { ...line, quantity: line.quantity + 1 } : line)
        : [...current, { product, variantId, quantity: 1 }];
      if (store) window.localStorage.setItem(cartKey(store.slug), JSON.stringify(next));
      return next;
    });
    setAddedProductId(product.id);
    setCartMessage((current) => ({ ...current, [product.id]: "Producto agregado al carrito." }));
    window.setTimeout(() => setAddedProductId(null), 1200);
  };

  if (error) return <main className="min-h-screen bg-[#f6f5f1] p-8 text-center text-[#171b14]"><h1 className="mt-24 text-4xl font-semibold">Tienda no disponible</h1><p className="mt-3 text-[#697066]">{error}</p></main>;
  if (!store) return <main className="min-h-screen bg-[#f6f5f1] p-8 text-center text-[#697066]">Cargando catálogo...</main>;

  return (
    <main className="min-h-screen bg-[#f6f5f1] text-[#171b14]">
      <header className="mx-auto flex max-w-6xl items-center justify-between px-5 py-6 sm:px-8">
        <div className="flex items-center gap-3">{store.logoUrl ? <img src={store.logoUrl} alt={`Logo de ${store.storeName}`} className="h-9 w-9 rounded-full object-cover" /> : <span className="flex h-9 w-9 items-center justify-center rounded-full bg-[#11150e] font-black text-white">V</span>}<div><div className="text-sm font-bold">{store.storeName}</div><div className="text-[10px] text-[#788073]">Catálogo online</div></div></div>
        <Link href={`/store/${store.slug}/cart`} className="inline-flex items-center gap-2 rounded-full bg-[#11150e] px-4 py-2 text-xs font-semibold text-white shadow-sm"><ShoppingBag className="h-3.5 w-3.5" /> Carrito ({cart.reduce((sum, line) => sum + line.quantity, 0)})</Link>
      </header>

      <section className="mx-auto max-w-6xl px-5 pb-14 pt-10 sm:px-8 sm:pt-20"><div className="max-w-3xl"><div className="text-[10px] font-bold uppercase tracking-[0.22em] text-[#75a832]">Compra directo por WhatsApp</div><h1 className="mt-4 text-5xl font-semibold leading-[0.9] tracking-[-0.07em] sm:text-7xl">Encontrá algo que<br /><span className="italic text-[#75a832]">te encante.</span></h1><p className="mt-6 max-w-xl text-sm leading-relaxed text-[#697066]">Explorá el catálogo de {store.storeName}, agregá productos al carrito o hablá con VANTA para resolver dudas.</p></div></section>

      <section id="catalogo" className="mx-auto max-w-6xl px-5 pb-20 sm:px-8">
        <div className="grid gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {store.products.map((product) => {
            const selected = selectedVariant[product.id];
            const variant = product.variants.find((item) => item.id === selected);
            const price = variant?.priceOverride ?? product.price;
            const image = imageUrl(product);
            const needsVariant = product.variants.length > 0 && !selected;
            return (
              <article key={product.id} className="overflow-hidden rounded-[24px] border border-[#dfe2d9] bg-white shadow-[0_16px_38px_rgba(30,39,24,0.06)]">
                <div className="relative aspect-[1.05] overflow-hidden bg-[#e8ece5]">{image ? <img src={image} alt={product.name} className="h-full w-full object-cover" /> : <div className="flex h-full items-center justify-center text-[#9aa493]"><ShoppingBag className="h-12 w-12" /></div>}<span className="absolute left-4 top-4 rounded-full bg-white/85 px-3 py-1 text-[10px] font-bold backdrop-blur">{product.variants.length ? `${product.variants.filter((item) => item.stockQuantity > 0).length} variantes` : "Disponible"}</span></div>
                <div className="p-5"><h2 className="text-lg font-bold">{product.name}</h2><p className="mt-2 min-h-10 text-xs leading-relaxed text-[#697066]">{product.description ?? "Producto seleccionado de nuestro catálogo."}</p>{product.variants.length > 0 && <select value={selected ?? ""} onChange={(event) => { setSelectedVariant((current) => ({ ...current, [product.id]: event.target.value })); setCartMessage((current) => ({ ...current, [product.id]: "" })); }} className="mt-4 w-full rounded-xl border border-[#dfe2d9] bg-[#f6f5f1] px-3 py-2 text-xs"><option value="">Elegí una variante</option>{product.variants.map((item) => <option key={item.id} value={item.id} disabled={item.stockQuantity <= 0}>{item.name} {item.stockQuantity <= 0 ? "· Sin stock" : `· ${item.stockQuantity} disponibles`}</option>)}</select>}<div className="mt-5 flex items-center justify-between gap-3"><div className="text-xl font-black">₲ {money(price)}</div><button type="button" disabled={needsVariant} onClick={() => addToCart(product)} className="inline-flex items-center gap-2 rounded-full bg-[#11150e] px-4 py-2.5 text-xs font-bold text-white transition-transform hover:-translate-y-0.5 disabled:cursor-not-allowed disabled:opacity-45">{addedProductId === product.id ? <><Check className="h-3.5 w-3.5" /> Agregado</> : <><ShoppingBag className="h-3.5 w-3.5" /> Agregar</>}</button></div>{cartMessage[product.id] && <p className={`mt-3 text-xs font-medium ${needsVariant ? "text-[#a63d28]" : "text-[#4f7537]"}`}>{cartMessage[product.id]}</p>}<a href={`https://wa.me/?text=${encodeURIComponent(`Hola, quiero consultar por ${product.name}${variant ? ` (${variant.name})` : ""} en ${store.storeName}.`)}`} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-2 text-xs font-semibold text-[#4f7537]"><MessageCircle className="h-3.5 w-3.5" /> Consultar por WhatsApp</a></div>
              </article>
            );
          })}
        </div>
      </section>
      <footer className="border-t border-[#dfe2d9] px-5 py-8 text-center text-[10px] text-[#788073]">Catálogo impulsado por VANTA · Ventas inteligentes por WhatsApp</footer>
    </main>
  );
}