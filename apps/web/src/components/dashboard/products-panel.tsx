"use client";

import { useEffect, useState } from "react";
import Image from "next/image";
import Link from "next/link";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ProductForm } from "@/components/dashboard/product-form";
import { VariantManager } from "@/components/dashboard/variant-manager";
import { Box, Search, Plus, Edit2, Power, Eye, ChevronDown, ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, Grid2X2, ListFilter, TableProperties, ArrowUpRight } from "lucide-react";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";

type Product = {
  id: string;
  name: string;
  description: string | null;
  productNumber: string;
  sku: string;
  price: number;
  discountPercent: number;
  isActive: boolean;
  imageBase64: string | null;
  imageMediaType: string | null;
  categoryId: string | null;
  categoryName?: string | null;
  availableSizes: string[];
  gender: string;
  createdAt: string;
};

type Variant = {
  id: string;
  name: string;
  sku: string;
  stockQuantity: number;
  priceOverride: number | null;
};

const currency = new Intl.NumberFormat("es-PY", {
  style: "currency",
  currency: "PYG",
  maximumFractionDigits: 0,
});

export function ProductsPanel() {
  const [products, setProducts] = useState<Product[]>([]);
  const [variants, setVariants] = useState<Variant[]>([]);
  const [selectedProductId, setSelectedProductId] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [pageSize, setPageSize] = useState(6);
  const [isFilterOpen, setIsFilterOpen] = useState(false);
  const [categoryFilter, setCategoryFilter] = useState("all");
  const [statusFilter, setStatusFilter] = useState<"all" | "active" | "inactive">("all");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [variantsLoading, setVariantsLoading] = useState(false);
  const [mode, setMode] = useState<"list" | "create" | "edit">("list");
  const [viewMode, setViewMode] = useState<"table" | "dash">("table");
  const [editingProduct, setEditingProduct] = useState<Product | null>(null);
  const [togglingProductId, setTogglingProductId] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const loadProducts = async () => {
    setIsLoading(true);
    const response = await fetch("/api/products", { cache: "no-store" });
    if (!response.ok) {
      setIsLoading(false);
      return;
    }
    setProducts((await response.json()) as Product[]);
    setIsLoading(false);
  };

  const loadVariants = async (productId: string) => {
    setVariantsLoading(true);
    const response = await fetch(`/api/products/${productId}/variants`, { cache: "no-store" });
    if (!response.ok) {
      setVariants([]);
      setVariantsLoading(false);
      return;
    }
    setVariants((await response.json()) as Variant[]);
    setVariantsLoading(false);
  };

  useEffect(() => {
    void Promise.resolve().then(loadProducts);
  }, []);

  useEffect(() => {
    if (selectedProductId) void Promise.resolve().then(() => loadVariants(selectedProductId));
  }, [selectedProductId]);

  const selectedProduct = products.find((p) => p.id === selectedProductId);
  const filteredProducts = products.filter((product) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = !normalizedSearch || `${product.name} ${product.sku} ${product.productNumber}`.toLowerCase().includes(normalizedSearch);
    const matchesCategory = categoryFilter === "all" || product.categoryId === categoryFilter;
    const matchesStatus = statusFilter === "all" || (statusFilter === "active" ? product.isActive : !product.isActive);
    const createdDate = product.createdAt.slice(0, 10);
    const matchesDateFrom = !dateFrom || createdDate >= dateFrom;
    const matchesDateTo = !dateTo || createdDate <= dateTo;
    return matchesSearch && matchesCategory && matchesStatus && matchesDateFrom && matchesDateTo;
  });
  const visibleProducts = filteredProducts.slice(0, pageSize);
  const categories = products.filter((product) => product.categoryId && product.categoryName).reduce<{ id: string; name: string }[]>((result, product) => result.some((category) => category.id === product.categoryId) ? result : [...result, { id: product.categoryId!, name: product.categoryName! }], []);

  const handleCreate = () => {
    setMode("create");
    setEditingProduct(null);
    setSelectedProductId(null);
  };

  const handleEdit = async (product: Product) => {
    await loadVariants(product.id);
    setEditingProduct(product);
    setMode("edit");
  };

  const handleFormSuccess = async () => {
    await loadProducts();
    setMode("list");
    setEditingProduct(null);
  };

  const handleToggleActive = async (product: Product) => {
    const action = product.isActive ? "desactivar" : "activar";
    if (!window.confirm(`¿Querés ${action} ${product.name}?`)) return;
    setTogglingProductId(product.id);
    setActionError(null);
    try {
      const response = await fetch(`/api/products/${product.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: product.name,
          description: product.description,
          price: product.price,
          discountPercent: product.discountPercent,
          categoryId: product.categoryId,
          availableSizes: product.availableSizes,
          gender: product.gender,
          isActive: !product.isActive,
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        setActionError(data?.error ?? `No se pudo ${action} el producto.`);
        return;
      }
      const updated = data as Product;
      setProducts((current) => current.map((item) => item.id === updated.id ? { ...item, ...updated } : item));
    } finally {
      setTogglingProductId(null);
    }
  };

  const totalStock = variants.reduce((acc, v) => acc + v.stockQuantity, 0);

  const formatProductId = (product: Product) => product.productNumber;

  return (
    <div className="w-full">
      {mode === "list" && (
        <section className="overflow-hidden rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] shadow-[0_12px_34px_rgba(24,22,20,0.04)]">
          <div className="flex flex-col gap-5 border-b border-[#eee8e4] px-4 pb-5 pt-2 sm:px-6 lg:px-7">
            <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
              <h1 className="text-[30px] font-semibold tracking-[-0.07em] text-[#1b1e21]">Lista de productos</h1>
              <div className="flex items-center gap-2">
                <button onClick={handleCreate} className="inline-flex items-center gap-2 rounded-full bg-[#a7edb3] px-4 py-2.5 text-[13px] font-medium text-[#1c3a28] shadow-[0_8px_18px_rgba(97,190,112,0.14)]">
                  Agregar producto <Plus className="size-4" />
                </button>
              </div>
            </div>

            <div className="flex flex-col justify-between gap-3 lg:flex-row lg:items-center">
              <div className="text-[16px] font-medium text-[#24282c]">{products.length.toLocaleString("es-PY")} productos</div>
              <div className="flex flex-wrap items-center gap-2 text-[12px] text-[#6e7479]">
                <label className="flex min-w-[180px] items-center gap-2 rounded-full bg-[#f1efed] px-3 py-2 text-[#858b90]">
                  <Search className="size-4" />
                  <Input type="text" placeholder="Buscar por nombre o SKU" value={search} onChange={(e) => setSearch(e.target.value)} className="h-auto border-0 bg-transparent p-0 text-[12px] shadow-none focus-visible:ring-0" />
                </label>
                <div className="flex items-center gap-1 rounded-full bg-[#f1efed] p-1">
                  <button type="button" aria-label="Vista de tabla" onClick={() => setViewMode("table")} className={`flex size-8 items-center justify-center rounded-full transition-colors ${viewMode === "table" ? "bg-white text-[#24282c] shadow-sm" : "text-[#858b90]"}`}><TableProperties className="size-4" /></button>
                  <button type="button" aria-label="Vista de tablero" onClick={() => setViewMode("dash")} className={`flex size-8 items-center justify-center rounded-full transition-colors ${viewMode === "dash" ? "bg-white text-[#24282c] shadow-sm" : "text-[#858b90]"}`}><Grid2X2 className="size-4" /></button>
                </div>
                <button type="button" aria-label="Filtrar productos" onClick={() => setIsFilterOpen((open) => !open)} className={`flex size-9 items-center justify-center rounded-full ${isFilterOpen || categoryFilter !== "all" || statusFilter !== "all" || dateFrom || dateTo ? "bg-[#a7edb3] text-[#23703b]" : "bg-[#f1efed] text-[#596066]"}`}><ListFilter className="size-4" /></button>
              </div>
            </div>
            {isFilterOpen && <div className="flex flex-wrap items-end gap-3 rounded-[14px] border border-[#e8e2de] bg-[#faf9f8] p-3 text-[12px]"><label className="flex min-w-44 flex-col gap-1 text-[#6e7479]">Categoría<select value={categoryFilter} onChange={(event) => setCategoryFilter(event.target.value)} className="h-9 rounded-lg border-0 bg-white px-2 text-[#30353a] shadow-sm"><option value="all">Todas las categorías</option>{categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}</select></label><label className="flex min-w-36 flex-col gap-1 text-[#6e7479]">Estado<select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)} className="h-9 rounded-lg border-0 bg-white px-2 text-[#30353a] shadow-sm"><option value="all">Todos</option><option value="active">Activos</option><option value="inactive">Inactivos</option></select></label><label className="flex flex-col gap-1 text-[#6e7479]">Desde<input type="date" value={dateFrom} onChange={(event) => setDateFrom(event.target.value)} className="h-9 rounded-lg border-0 bg-white px-2 text-[#30353a] shadow-sm" /></label><label className="flex flex-col gap-1 text-[#6e7479]">Hasta<input type="date" value={dateTo} min={dateFrom || undefined} onChange={(event) => setDateTo(event.target.value)} className="h-9 rounded-lg border-0 bg-white px-2 text-[#30353a] shadow-sm" /></label><button type="button" onClick={() => { setCategoryFilter("all"); setStatusFilter("all"); setDateFrom(""); setDateTo(""); }} className="h-9 rounded-full px-3 text-[#6f39cf] hover:bg-white">Limpiar filtros</button></div>}
          </div>

          {isLoading && <div className="space-y-3 p-6"><Skeleton className="h-14" /><Skeleton className="h-14" /><Skeleton className="h-14" /></div>}
          {actionError && <p className="mx-6 mt-4 rounded-lg bg-[#fff0f0] px-3 py-2 text-sm text-[#b55757]">{actionError}</p>}
          {!isLoading && filteredProducts.length === 0 && <EmptyState icon={Box} title={search ? "Sin resultados" : "Tu catálogo está vacío"} description={search ? "Probá con otro nombre o SKU." : "Creá tu primer producto para empezar a vender."} className="min-h-44 border-0 bg-transparent px-6 py-8" />}
          {!isLoading && filteredProducts.length > 0 && viewMode === "dash" && (
            <div className="grid grid-cols-1 gap-4 p-4 sm:grid-cols-2 sm:p-6 lg:grid-cols-3 lg:p-7">
              {visibleProducts.map((product) => (
                <div key={product.id} className="flex flex-col gap-4 rounded-[18px] border border-[#eee9e5] bg-white p-4 shadow-sm">
                  <div className="flex items-start gap-3">
                    <div className="flex size-16 shrink-0 items-center justify-center overflow-hidden rounded-[10px] bg-[#e9e7e5]">
                      {product.imageBase64 ? <Image src={`data:${product.imageMediaType || "image/jpeg"};base64,${product.imageBase64}`} alt={product.name} width={64} height={64} unoptimized className="size-full object-cover" /> : <Box className="size-5 text-[#8b8f91]" />}
                    </div>
                    <div className="min-w-0"><p className="truncate text-[14px] font-semibold text-[#1b1e21]">{product.name}</p><p className="mt-1 text-[11px] text-[#9a9da0]">{product.productNumber} · {product.sku}</p></div>
                  </div>
                  <div className="grid grid-cols-2 gap-2 text-[12px] text-[#4c5257]"><span>Categoría<br /><strong>{product.categoryName || "Sin categoría"}</strong></span><span>Precio<br /><strong>{currency.format(product.price)}</strong></span><span>Descuento<br /><strong>{product.discountPercent}%</strong></span><span>Estado<br /><strong>{product.isActive ? "Activo" : "Inactivo"}</strong></span></div>
                  <div className="flex items-center justify-between border-t border-[#eee9e5] pt-3"><span className="text-[11px] text-[#9a9da0]">Producto del catálogo</span><div className="flex gap-2"><Link href={`/dashboard/products/${product.id}`} aria-label={`Ver ${product.name}`} className="flex size-8 items-center justify-center rounded-full border border-[#e5dfdc] text-[#555c62] hover:text-[#6f39cf]"><ArrowUpRight className="size-3.5" /></Link><button type="button" aria-label={`Editar ${product.name}`} onClick={() => void handleEdit(product)} className="flex size-8 items-center justify-center rounded-full border border-[#e5dfdc] text-[#555c62] hover:text-[#6f39cf]"><Edit2 className="size-3.5" /></button></div></div>
                </div>
              ))}
            </div>
          )}

          {!isLoading && filteredProducts.length > 0 && viewMode === "table" && (
            <div className="overflow-x-auto">
              <table className="min-w-[850px] w-full border-separate border-spacing-0 text-left">
                <thead>
                  <tr className="text-[12px] font-semibold text-[#33383c]">
                    <th className="px-6 py-4 lg:px-7">ID del producto</th>
                    <th className="px-4 py-4">Producto</th>
                    <th className="px-4 py-4">Categoría</th>
                    <th className="px-4 py-4">Precio</th>
                    <th className="px-4 py-4">Estado</th>
                    <th className="px-6 py-4 text-right lg:px-7">Acciones</th>
                  </tr>
                </thead>
                <tbody>
                  {visibleProducts.map((product, index) => (
                    <tr key={product.id} onClick={() => setSelectedProductId(product.id)} className={`group text-[12px] text-[#3f4549] ${index % 2 === 0 ? "bg-[#f1f1f1]" : "bg-transparent"}`}>
                      <td className={`px-6 py-4 lg:px-7 ${index === 0 ? "rounded-l-[24px]" : ""}`}>{formatProductId(product)}</td>
                      <td className="px-4 py-4">
                        <div className="flex items-center gap-3">
                          <div className="flex size-9 shrink-0 items-center justify-center overflow-hidden rounded-[8px] bg-[#e9e7e5]">
                            {product.imageBase64 ? <Image src={`data:${product.imageMediaType || "image/jpeg"};base64,${product.imageBase64}`} alt={product.name} width={36} height={36} unoptimized className="size-full object-cover" /> : <Box className="size-4 text-[#8b8f91]" />}
                          </div>
                          <span className="max-w-[180px] font-medium text-[#252a2e]">{product.name}</span>
                        </div>
                      </td>
                      <td className="px-4 py-4">{product.categoryName || "Sin categoría"}</td>
                      <td className="px-4 py-4 font-medium">{currency.format(product.price)}</td>
                      <td className="px-4 py-4"><span className={`inline-flex rounded-full px-3 py-1 text-[11px] font-medium ${product.isActive ? "bg-[#a8edb3] text-[#23703b]" : "bg-[#ffd0d0] text-[#a44343]"}`}>{product.isActive ? "Activo" : "Inactivo"}</span></td>
                      <td className={`px-6 py-4 text-right lg:px-7 ${index === 0 ? "rounded-r-[24px]" : ""}`}>
                        <div className="flex justify-end gap-2">
                          <Link href={`/dashboard/products/${product.id}`} aria-label={`Ver ${product.name}`} onClick={(event) => event.stopPropagation()} className="flex size-8 items-center justify-center rounded-full bg-white text-[#555c62] shadow-sm hover:text-[#6f39cf]"><Eye className="size-3.5" /></Link>
                          <button aria-label={`Editar ${product.name}`} onClick={(event) => { event.stopPropagation(); void handleEdit(product); }} className="flex size-8 items-center justify-center rounded-full bg-white text-[#555c62] shadow-sm hover:text-[#6f39cf]"><Edit2 className="size-3.5" /></button>
                          <button type="button" aria-label={`${product.isActive ? "Desactivar" : "Activar"} ${product.name}`} title={product.isActive ? "Desactivar producto" : "Activar producto"} onClick={(event) => { event.stopPropagation(); void handleToggleActive(product); }} disabled={togglingProductId === product.id} className={`flex size-8 items-center justify-center rounded-full shadow-sm disabled:opacity-50 ${product.isActive ? "bg-[#ffd0d0] text-[#a44343]" : "bg-[#a8edb3] text-[#23703b]"}`}><Power className="size-3.5" /></button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          <div className="flex flex-col justify-between gap-3 border-t border-[#eee8e4] px-6 py-4 text-[12px] text-[#5e666d] sm:flex-row sm:items-center lg:px-7">
            <div className="flex flex-wrap items-center gap-2"><span>Mostrar</span><select value={pageSize} onChange={(event) => setPageSize(Number(event.target.value))} className="rounded-full border-0 bg-[#f1efed] px-3 py-2 text-[#41474b] outline-none"><option value={6}>6</option><option value={12}>12</option><option value={24}>24</option></select><span>productos · Mostrando {Math.min(visibleProducts.length, pageSize)} de {filteredProducts.length}</span></div>
            <div className="flex items-center gap-1">
              <button aria-label="Primera página" className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]"><ChevronsLeft className="size-3.5" /></button>
              <button aria-label="Página anterior" className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]"><ChevronLeft className="size-3.5" /></button>
              <button className="flex size-8 items-center justify-center rounded-[8px] bg-[#a7edb3] font-semibold text-[#23703b]">1</button>
              <button className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]">2</button>
              <button className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]">3</button>
              <span className="px-1">...</span>
              <button className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]">10</button>
              <button aria-label="Página siguiente" className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]"><ChevronRight className="size-3.5" /></button>
              <button aria-label="Última página" className="flex size-8 items-center justify-center rounded-[8px] bg-[#f1efed]"><ChevronsRight className="size-3.5" /></button>
            </div>
          </div>
        </section>
      )}

      {(mode === "create" || mode === "edit") && (
        <div className="w-full">
          <button onClick={() => { setMode("list"); setEditingProduct(null); }} className="mb-4 text-left text-xs text-[#737a80] hover:text-[#24282c]">← Volver a productos</button>
          <ProductForm mode={mode} product={editingProduct ?? undefined} initialStock={variants.reduce((total, variant) => total + variant.stockQuantity, 0)} onSuccess={handleFormSuccess} onCreated={(product) => { setSelectedProductId(product.id); void loadVariants(product.id); }} />
        </div>
      )}

      {mode === "list" && selectedProduct && (
        <div className="mt-4 flex flex-col gap-4 rounded-[20px] border border-[#e8e2de] bg-[#fbfaf9] p-4 shadow-sm sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold text-[#24282c]">{selectedProduct.name}</p>
            <p className="mt-1 text-xs text-muted-foreground">{selectedProduct.sku} · {currency.format(selectedProduct.price)} · {totalStock} en stock</p>
          </div>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => void handleEdit(selectedProduct)}><Edit2 className="mr-2 size-3.5" /> Editar</Button>
            <VariantManager productId={selectedProduct.id} productPrice={selectedProduct.price} onVariantAdded={() => void loadVariants(selectedProduct.id)} />
          </div>
        </div>
      )}
    </div>
  );
}
