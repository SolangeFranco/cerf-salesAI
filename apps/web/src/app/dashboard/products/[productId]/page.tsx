"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ProductForm, type ProductFormProduct } from "@/components/dashboard/product-form";

type Variant = { stockQuantity: number };

export default function ProductDetailPage({ params }: { params: Promise<{ productId: string }> }) {
  const [product, setProduct] = useState<ProductFormProduct | null>(null);
  const [stock, setStock] = useState(0);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void params.then(async ({ productId }) => {
      const [productResponse, variantsResponse] = await Promise.all([
        fetch(`/api/products/${productId}`, { cache: "no-store" }),
        fetch(`/api/products/${productId}/variants`, { cache: "no-store" }),
      ]);
      if (!productResponse.ok) throw new Error(productResponse.status === 404 ? "Producto no encontrado." : "No se pudo cargar el producto.");
      setProduct((await productResponse.json()) as ProductFormProduct);
      if (variantsResponse.ok) {
        const variants = (await variantsResponse.json()) as Variant[];
        setStock(variants.reduce((total, variant) => total + variant.stockQuantity, 0));
      }
    }).catch((reason: Error) => setError(reason.message));
  }, [params]);

  if (error) return <div className="flex flex-col gap-4"><Link href="/dashboard/products" className="text-sm text-muted-foreground hover:text-foreground">Volver a productos</Link><p className="text-sm text-destructive">{error}</p></div>;
  if (!product) return <p className="text-sm text-muted-foreground">Cargando producto...</p>;

  return <div className="w-full"><Link href="/dashboard/products" className="mb-4 inline-flex text-xs text-[#737a80] hover:text-[#24282c]">← Volver a productos</Link><ProductForm mode="edit" product={product} readOnly initialStock={stock} /></div>;
}
