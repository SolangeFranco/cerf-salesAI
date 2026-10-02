"use client";

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";

type Product = { id: string; name: string; sku: string; price: number };
type Customer = { id: string; name: string; phone: string };
type CartLine = Product & { quantity: number };
type CreatedOrder = { number: string; total: number; status: string };

const currency = new Intl.NumberFormat("es-PY", {
  style: "currency",
  currency: "PYG",
  maximumFractionDigits: 0,
});

export default function NewOrderPage() {
  const [products, setProducts] = useState<Product[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [cart, setCart] = useState<CartLine[]>([]);
  const [customerId, setCustomerId] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [createdOrder, setCreatedOrder] = useState<CreatedOrder | null>(null);

  useEffect(() => {
    Promise.all([
      fetch("/api/products", { cache: "no-store" }),
      fetch("/api/customers", { cache: "no-store" }),
    ])
      .then(async ([productsResponse, customersResponse]) => {
        if (!productsResponse.ok) throw new Error("No se pudo cargar el catálogo.");
        if (!customersResponse.ok) throw new Error("No se pudieron cargar los clientes.");
        setProducts((await productsResponse.json()) as Product[]);
        setCustomers((await customersResponse.json()) as Customer[]);
      })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setIsLoading(false));
  }, []);

  const total = useMemo(
    () => cart.reduce((sum, line) => sum + line.price * line.quantity, 0),
    [cart],
  );

  const addProduct = (product: Product) => {
    setCreatedOrder(null);
    setCart((current) => {
      const existing = current.find((line) => line.id === product.id);
      if (existing) {
        return current.map((line) => line.id === product.id ? { ...line, quantity: line.quantity + 1 } : line);
      }
      return [...current, { ...product, quantity: 1 }];
    });
  };

  const updateQuantity = (productId: string, quantity: number) => {
    setCart((current) => current
      .map((line) => line.id === productId ? { ...line, quantity } : line)
      .filter((line) => line.quantity > 0));
  };

  const submitOrder = async () => {
    if (cart.length === 0) return;
    setError(null);
    setIsSubmitting(true);
    const idempotencyKey = `web-order-${crypto.randomUUID()}`;
    try {
      const response = await fetch("/api/orders", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": idempotencyKey },
        body: JSON.stringify({
          customerId: customerId || null,
          items: cart.map((line) => ({ productId: line.id, quantity: line.quantity })),
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        setError(data?.error ?? "No se pudo crear el pedido.");
        return;
      }
      setCreatedOrder(data as CreatedOrder);
      setCart([]);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="flex flex-col gap-8">
      <div className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Nuevo pedido</h1>
          <p className="text-muted-foreground">Armá un pedido desde tu catálogo para probar el flujo comercial.</p>
        </div>
        <Link href="/dashboard/orders" className={cn(buttonVariants({ variant: "outline" }))}>Volver a pedidos</Link>
      </div>

      {error && <p className="text-sm text-destructive">{error}</p>}
      {createdOrder && (
        <div className="rounded-md border border-emerald-600/30 bg-emerald-50 px-4 py-3 text-sm text-emerald-900">
          Pedido {createdOrder.number} creado correctamente por {currency.format(createdOrder.total)}.
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Catálogo</CardTitle>
            <CardDescription>Elegí productos para agregarlos al pedido.</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading && <p className="py-8 text-sm text-muted-foreground">Cargando catálogo...</p>}
            {!isLoading && products.length === 0 && <p className="py-8 text-sm text-muted-foreground">Agregá productos antes de crear un pedido.</p>}
            <div className="divide-y">
              {products.map((product) => (
                <div key={product.id} className="flex items-center justify-between gap-4 py-4">
                  <div>
                    <p className="font-medium">{product.name}</p>
                    <p className="text-sm text-muted-foreground">{product.sku} · {currency.format(product.price)}</p>
                  </div>
                  <button type="button" onClick={() => addProduct(product)} className="rounded-lg border px-3 py-1.5 text-sm font-medium hover:bg-muted">Agregar</button>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-lg">Carrito</CardTitle>
            <CardDescription>{cart.length} productos diferentes.</CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            <label className="flex flex-col gap-2 text-sm font-medium" htmlFor="order-customer">
              Cliente <span className="font-normal text-muted-foreground">(opcional)</span>
              <select id="order-customer" value={customerId} onChange={(event) => setCustomerId(event.target.value)} className="h-9 rounded-lg border bg-background px-3 text-sm font-normal">
                <option value="">Sin cliente asociado</option>
                {customers.map((customer) => <option key={customer.id} value={customer.id}>{customer.name} · {customer.phone}</option>)}
              </select>
            </label>
            {cart.length === 0 && <p className="py-6 text-sm text-muted-foreground">El carrito está vacío.</p>}
            {cart.map((line) => (
              <div key={line.id} className="flex flex-col gap-2 border-b pb-3">
                <div className="flex justify-between gap-3 text-sm">
                  <span className="font-medium">{line.name}</span>
                  <span>{currency.format(line.price * line.quantity)}</span>
                </div>
                <Input type="number" min="0" value={line.quantity} onChange={(event) => updateQuantity(line.id, Number(event.target.value))} aria-label={`Cantidad de ${line.name}`} />
              </div>
            ))}
            <div className="flex items-center justify-between border-t pt-4 font-semibold">
              <span>Total</span>
              <span>{currency.format(total)}</span>
            </div>
            <button type="button" disabled={isSubmitting || cart.length === 0} onClick={() => void submitOrder()} className="inline-flex h-9 items-center justify-center rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/80 disabled:pointer-events-none disabled:opacity-50">
              {isSubmitting ? "Creando pedido..." : "Confirmar pedido"}
            </button>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
