"use client";

import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type Variant = {
  id: string;
  name: string;
  sku: string;
  stockQuantity: number;
  priceOverride: number | null;
};

type VariantManagerProps = {
  productId: string;
  productPrice: number;
  onVariantAdded?: () => void;
};

const currency = new Intl.NumberFormat("es-PY", {
  style: "currency",
  currency: "PYG",
  maximumFractionDigits: 0,
});

export function VariantManager({ productId, productPrice, onVariantAdded }: VariantManagerProps) {
  const [variants, setVariants] = useState<Variant[]>([]);
  const [values, setValues] = useState({ name: "", sku: "", stockQuantity: "0", priceOverride: "" });
  const [isOpen, setIsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadVariants = async () => {
    setIsLoading(true);
    const response = await fetch(`/api/products/${productId}/variants`, { cache: "no-store" });
    if (!response.ok) {
      setError("No se pudieron cargar las variantes.");
      setIsLoading(false);
      return;
    }
    setVariants((await response.json()) as Variant[]);
    setIsLoading(false);
  };

  const toggle = () => {
    const nextOpen = !isOpen;
    setIsOpen(nextOpen);
    if (nextOpen && variants.length === 0) void loadVariants();
  };

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      const response = await fetch(`/api/products/${productId}/variants`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: values.name,
          sku: values.sku,
          stockQuantity: Number(values.stockQuantity),
          priceOverride: values.priceOverride ? Number(values.priceOverride) : null,
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        setError(data?.error ?? "No se pudo crear la variante.");
        return;
      }
      setValues({ name: "", sku: "", stockQuantity: "0", priceOverride: "" });
      await loadVariants();
      onVariantAdded?.();
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="mt-3 border-t pt-3">
      <button type="button" onClick={toggle} className="text-sm font-medium text-muted-foreground hover:text-foreground">
        {isOpen ? "Ocultar variantes" : "Ver y agregar variantes"}
      </button>
      {isOpen && (
        <div className="mt-3 flex flex-col gap-4">
          {isLoading && <p className="text-sm text-muted-foreground">Cargando variantes...</p>}
          {!isLoading && variants.length === 0 && <p className="text-sm text-muted-foreground">Sin variantes. Precio base: {currency.format(productPrice)}</p>}
          {variants.length > 0 && (
            <div className="divide-y rounded-md border px-3">
              {variants.map((variant) => (
                <div key={variant.id} className="flex items-center justify-between gap-4 py-2 text-sm">
                  <div>
                    <p className="font-medium">{variant.name}</p>
                    <p className="text-muted-foreground">{variant.sku}</p>
                  </div>
                  <div className="text-right">
                    <p>{variant.stockQuantity} en stock</p>
                    <p className="text-muted-foreground">{currency.format(variant.priceOverride ?? productPrice)}</p>
                  </div>
                </div>
              ))}
            </div>
          )}
          <form onSubmit={submit} className="grid gap-3 rounded-md bg-muted/40 p-3 sm:grid-cols-2">
            <div className="flex flex-col gap-1 sm:col-span-2">
              <Label htmlFor={`variant-name-${productId}`}>Nombre de variante</Label>
              <Input id={`variant-name-${productId}`} value={values.name} onChange={(event) => setValues({ ...values, name: event.target.value })} placeholder="Talle M / Negro" required />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor={`variant-sku-${productId}`}>SKU</Label>
              <Input id={`variant-sku-${productId}`} value={values.sku} onChange={(event) => setValues({ ...values, sku: event.target.value })} placeholder="CAM-M-NEG" required />
            </div>
            <div className="flex flex-col gap-1">
              <Label htmlFor={`variant-stock-${productId}`}>Stock</Label>
              <Input id={`variant-stock-${productId}`} type="number" min="0" value={values.stockQuantity} onChange={(event) => setValues({ ...values, stockQuantity: event.target.value })} required />
            </div>
            <div className="flex flex-col gap-1 sm:col-span-2">
              <Label htmlFor={`variant-price-${productId}`}>Precio especial <span className="text-muted-foreground">(opcional)</span></Label>
              <Input id={`variant-price-${productId}`} type="number" min="0" value={values.priceOverride} onChange={(event) => setValues({ ...values, priceOverride: event.target.value })} placeholder={String(productPrice)} />
            </div>
            {error && <p className="text-sm text-destructive sm:col-span-2">{error}</p>}
            <button type="submit" disabled={isSubmitting} className="inline-flex h-9 items-center justify-center rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground hover:bg-primary/80 disabled:pointer-events-none disabled:opacity-50 sm:col-span-2">
              {isSubmitting ? "Guardando..." : "Agregar variante"}
            </button>
          </form>
        </div>
      )}
    </div>
  );
}
