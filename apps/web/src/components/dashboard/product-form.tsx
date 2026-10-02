"use client";

import { useEffect, useState } from "react";
import { z } from "zod";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ImageUploader } from "@/components/dashboard/image-uploader";
import { Check, ImagePlus, PackagePlus, Plus } from "lucide-react";

const productSchema = z.object({
  name: z.string().min(2, "Ingresá un nombre."),
  sku: z.string().min(1, "Ingresá un SKU."),
  price: z.coerce.number().nonnegative("El precio no puede ser negativo."),
  description: z.string().max(2000, "La descripción es demasiado larga."),
  categoryId: z.string().optional(),
});

type Category = { id: string; name: string };
type ProductFormValues = { name: string; sku: string; price: string; description: string; categoryId: string };
type ProductImage = { id: string; imageBase64: string | null; mediaType: string; sortOrder: number };

export type ProductFormProduct = {
  id: string;
  name: string;
  description: string | null;
  productNumber: string;
  sku: string;
  price: number;
  discountPercent: number;
  isActive: boolean;
  categoryId: string | null;
  availableSizes: string[];
  gender: string;
  imageBase64?: string | null;
  imageMediaType?: string | null;
  images?: ProductImage[];
};

type ProductFormProps = {
  mode?: "create" | "edit";
  product?: ProductFormProduct;
  onSuccess?: () => void;
  onCreated?: (product: ProductFormProduct) => void;
  readOnly?: boolean;
  initialStock?: number;
};

function generateSku(name: string) {
  const value = name.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toUpperCase().replace(/[^A-Z0-9]+/g, "-").replace(/^-|-$/g, "").slice(0, 60);
  return value || "PRODUCTO";
}

export function ProductForm({ mode = "create", product, onSuccess, onCreated, readOnly = false, initialStock = 0 }: ProductFormProps) {
  const [values, setValues] = useState<ProductFormValues>({ name: product?.name ?? "", sku: product?.sku ?? "", price: product?.price.toString() ?? "0", description: product?.description ?? "", categoryId: product?.categoryId ?? "" });
  const [error, setError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSkuEdited, setIsSkuEdited] = useState(Boolean(product));
  const [selectedImages, setSelectedImages] = useState<File[]>([]);
  const [sizes, setSizes] = useState<string[]>(product?.availableSizes ?? []);
  const [gender, setGender] = useState("Unisex");
  const [selectedImageUrl, setSelectedImageUrl] = useState<string | null>(null);
  const [stock, setStock] = useState(String(initialStock));
  const [discount, setDiscount] = useState(String(product?.discountPercent ?? 0));
  const [categories, setCategories] = useState<Category[]>([]);
  const [newCategory, setNewCategory] = useState("");
  const [isCreatingCategory, setIsCreatingCategory] = useState(false);

  const update = (field: keyof ProductFormValues, value: string) => setValues((current) => ({ ...current, [field]: value }));
  const handleNameChange = (name: string) => { if (!readOnly) setValues((current) => ({ ...current, name, sku: isSkuEdited ? current.sku : generateSku(name) })); };

  useEffect(() => {
    void fetch("/api/products/categories", { cache: "no-store" }).then((response) => response.ok ? response.json() as Promise<Category[]> : []).then(setCategories);
  }, []);

  useEffect(() => {
    if (!product) return;
    setValues({ name: product.name, sku: product.sku, price: product.price.toString(), description: product.description ?? "", categoryId: product.categoryId ?? "" });
    setSizes(product.availableSizes ?? []);
    setGender(product.gender ?? "Unisex");
    setDiscount(String(product.discountPercent ?? 0));
  }, [product]);

  const createCategory = async () => {
    if (readOnly || !newCategory.trim()) return;
    setIsCreatingCategory(true);
    setError(null);
    try {
      const response = await fetch("/api/products/categories", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ name: newCategory.trim() }) });
      const data = await response.json().catch(() => null);
      if (!response.ok) { setError(data?.error ?? "No se pudo crear la categoría."); return; }
      const category = data as Category;
      setCategories((current) => [...current, category].sort((left, right) => left.name.localeCompare(right.name)));
      update("categoryId", category.id);
      setNewCategory("");
    } finally { setIsCreatingCategory(false); }
  };

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (readOnly) return;
    const parsed = productSchema.safeParse(values);
    if (!parsed.success) { setError(parsed.error.issues[0]?.message ?? "Revisá los datos."); return; }
    setError(null);
    setIsSubmitting(true);
    try {
      const response = await fetch(mode === "edit" && product ? `/api/products/${product.id}` : "/api/products", {
        method: mode === "edit" ? "PUT" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ ...parsed.data, categoryId: parsed.data.categoryId || null, availableSizes: sizes, gender, discountPercent: Number(discount) || 0, ...(mode === "edit" && product ? { isActive: product.isActive } : {}) }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) { setError(data?.error ?? "No se pudo guardar el producto."); return; }
      const savedProduct = data as ProductFormProduct;
      const stockResponse = await fetch(`/api/products/${savedProduct.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ stockQuantity: Number(stock), sizes }) });
      if (!stockResponse.ok) { const stockError = await stockResponse.json().catch(() => null); setError(stockError?.error ?? "El producto se guardó, pero no se pudo guardar el stock."); return; }
      if (selectedImages.length > 0) {
        const formData = new FormData();
        selectedImages.forEach((file) => formData.append("files", file));
        const imageResponse = await fetch(`/api/products/${savedProduct.id}/images`, { method: "POST", body: formData });
        if (!imageResponse.ok) { const imageError = await imageResponse.json().catch(() => null); setError(imageError?.error ?? "El producto se guardó, pero no se pudo subir la imagen."); return; }
      }
      onSuccess?.();
      if (mode === "create") onCreated?.(savedProduct);
    } finally { setIsSubmitting(false); }
  };

  const imageList = product?.images ?? (product?.imageBase64 ? [{ id: "primary", imageBase64: product.imageBase64, mediaType: product.imageMediaType ?? "image/jpeg", sortOrder: 0 }] : []);

  return (
    <form id="product-form" onSubmit={submit} className="flex flex-col gap-5">
      <div className="flex flex-col justify-between gap-4 border-b border-[#eee8e4] pb-5 sm:flex-row sm:items-center">
        <div className="flex items-center gap-3"><span className="flex size-9 items-center justify-center rounded-[10px] bg-[#1b1d20] text-white"><PackagePlus className="size-4" /></span><div><h1 className="text-[25px] font-semibold tracking-[-0.06em] text-[#1b1e21]">{readOnly ? "Consultar producto" : mode === "edit" ? "Editar producto" : "Agregar producto"}</h1><p className="mt-1 text-[12px] text-[#7b8186]">Completá la información para sumar un producto al catálogo.</p>{error && <p className="mt-2 rounded-[8px] bg-[#fff0f0] px-3 py-2 text-[12px] font-medium text-[#b55757]">{error}</p>}</div></div>
        {!readOnly && <button type="submit" disabled={isSubmitting} className="inline-flex items-center gap-2 rounded-full bg-[#a7edb3] px-4 py-2.5 text-[13px] font-medium text-[#1c3a28] disabled:opacity-50"><Check className="size-4" />{isSubmitting ? "Guardando..." : mode === "edit" ? "Guardar cambios" : "Agregar producto"}</button>}
      </div>

      <div className="grid gap-5 xl:grid-cols-[1.65fr_0.95fr]"><div className="space-y-5">
        <section className="rounded-[22px] border border-[#eee9e5] bg-[#faf9f8] p-4 sm:p-5"><h2 className="text-[17px] font-semibold text-[#202428]">Información general</h2><div className="mt-5 space-y-4">
          <div className="flex flex-col gap-2"><Label htmlFor="product-name">Nombre del producto</Label><Input id="product-name" value={values.name} onChange={(event) => handleNameChange(event.target.value)} disabled={readOnly} placeholder="Camisa de lino" /></div>
          <div className="flex flex-col gap-2"><Label htmlFor="product-description">Descripción del producto</Label><textarea id="product-description" value={values.description} onChange={(event) => update("description", event.target.value)} disabled={readOnly} placeholder="Material, talle, color..." rows={5} className="resize-none rounded-[11px] border-0 bg-[#f0f0f0] px-3 py-3 text-[13px] disabled:opacity-70" /></div>
          <div className="grid gap-4 sm:grid-cols-2"><div><Label>Talle</Label><p className="mt-1 text-[11px] text-[#9a9da0]">Elegí el talle disponible</p><div className="mt-2 flex flex-wrap gap-2">{["XS", "S", "M", "L", "XL", "XXL"].map((size) => <button key={size} type="button" disabled={readOnly} onClick={() => setSizes((current) => current.includes(size) ? current.filter((item) => item !== size) : [...current, size])} className={`flex size-11 items-center justify-center rounded-[6px] text-[12px] font-medium ${sizes.includes(size) ? "bg-[#a7edb3] text-[#25623a]" : "bg-[#eeeeee] text-[#4c5358]"}`}>{size}</button>)}</div></div><div><Label>Género</Label><p className="mt-1 text-[11px] text-[#9a9da0]">Elegí el género</p><div className="mt-4 flex flex-wrap gap-3 text-[12px]">{["Hombre", "Mujer", "Unisex"].map((option) => <button key={option} type="button" disabled={readOnly} onClick={() => setGender(option)} className="inline-flex items-center gap-1.5 disabled:opacity-60"><span className={`flex size-4 items-center justify-center rounded-full border-2 ${gender === option ? "border-[#75d989]" : "border-[#62686c]"}`}>{gender === option && <span className="size-2 rounded-full bg-[#75d989]" />}</span>{option}</button>)}</div></div></div>
          <div className="grid gap-4 sm:grid-cols-2"><div className="flex flex-col gap-2"><Label htmlFor="product-sku">SKU</Label><Input id="product-sku" value={values.sku} onChange={(event) => { setIsSkuEdited(true); update("sku", event.target.value); }} disabled={readOnly || mode === "edit"} /><p className="text-[11px] text-[#858b90]">Se genera automáticamente desde el nombre.</p></div><div className="flex flex-col gap-2"><Label htmlFor="product-price">Precio base</Label><Input id="product-price" type="number" value={values.price} onChange={(event) => update("price", event.target.value)} disabled={readOnly} /></div></div>
        </div></section>
        <section className="rounded-[22px] border border-[#eee9e5] bg-[#faf9f8] p-4 sm:p-5"><h2 className="text-[17px] font-semibold text-[#202428]">Precio y stock</h2><div className="mt-5 grid gap-4 sm:grid-cols-2"><div className="flex flex-col gap-2"><Label htmlFor="product-stock">Stock</Label><Input id="product-stock" type="number" value={stock} onChange={(event) => setStock(event.target.value)} disabled={readOnly} /></div><div className="flex flex-col gap-2"><Label htmlFor="product-discount">Descuento</Label><Input id="product-discount" value={discount} onChange={(event) => setDiscount(event.target.value)} disabled={readOnly} /></div></div></section>
      </div><div className="space-y-5">
        <section className="rounded-[22px] border border-[#eee9e5] bg-[#faf9f8] p-4 sm:p-5"><h2 className="text-[17px] font-semibold text-[#202428]">Imagen del producto</h2>{readOnly ? <div className="mt-4 grid grid-cols-3 gap-2">{imageList.length > 0 ? imageList.map((image) => <button type="button" key={image.id} onClick={() => setSelectedImageUrl(`data:${image.mediaType};base64,${image.imageBase64}`)} className="cursor-zoom-in"><img src={`data:${image.mediaType};base64,${image.imageBase64}`} alt={values.name} className="aspect-square w-full rounded-[8px] object-cover" /></button>) : <div className="col-span-3 flex min-h-[235px] items-center justify-center text-sm text-muted-foreground">Este producto no tiene imágenes.</div>}</div> : mode === "edit" && product ? <ImageUploader productId={product.id} /> : <label className="mt-4 flex min-h-[235px] cursor-pointer flex-col items-center justify-center rounded-[11px] bg-[#efefef] p-4 text-center text-[#8a9094]"><ImagePlus className="size-8" /><p className="mt-3 text-[12px] font-medium">Seleccioná una o varias imágenes</p><input type="file" accept="image/jpeg,image/png,image/webp" multiple className="sr-only" onChange={(event) => setSelectedImages(Array.from(event.target.files ?? []))} />{selectedImages.length > 0 && <div className="mt-3 grid grid-cols-3 gap-2">{selectedImages.map((file) => <button type="button" key={`${file.name}-${file.lastModified}`} onClick={(event) => event.preventDefault()} className="cursor-zoom-in"><img src={URL.createObjectURL(file)} alt={file.name} className="aspect-square w-20 rounded object-cover" /></button>)}</div>}</label>}</section>
        <section className="rounded-[22px] border border-[#eee9e5] bg-[#faf9f8] p-4 sm:p-5"><h2 className="text-[17px] font-semibold text-[#202428]">Categoría</h2><Label htmlFor="product-category" className="mt-5 block">Categoría del producto</Label><select id="product-category" value={values.categoryId} onChange={(event) => update("categoryId", event.target.value)} disabled={readOnly} className="mt-2 h-11 w-full rounded-[11px] bg-[#f0f0f0] px-3 disabled:opacity-70"><option value="">Sin categoría</option>{categories.map((category) => <option key={category.id} value={category.id}>{category.name}</option>)}</select>{!readOnly && <><div className="mt-4 flex gap-2"><Input value={newCategory} onChange={(event) => setNewCategory(event.target.value)} placeholder="Nueva categoría" /><button type="button" onClick={() => void createCategory()} disabled={isCreatingCategory || !newCategory.trim()} className="inline-flex items-center gap-1 rounded-full bg-[#a7edb3] px-3 text-xs"><Plus className="size-3.5" />Agregar</button></div><p className="mt-2 text-[11px] text-[#858b90]">Creá una categoría aquí para tus próximos productos.</p></>}</section>
      </div></div>
      {selectedImageUrl && <div role="dialog" aria-modal="true" className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-6" onClick={() => setSelectedImageUrl(null)}><button type="button" aria-label="Cerrar imagen ampliada" className="absolute right-5 top-5 rounded-full bg-white/90 px-3 py-2 text-xl text-black" onClick={() => setSelectedImageUrl(null)}>×</button><img src={selectedImageUrl} alt={values.name} className="max-h-[90vh] max-w-[90vw] rounded-lg object-contain" onClick={(event) => event.stopPropagation()} /></div>}
    </form>
  );
}
