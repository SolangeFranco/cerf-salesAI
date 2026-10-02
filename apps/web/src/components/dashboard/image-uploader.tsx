"use client";

import { useEffect, useState } from "react";
import Image from "next/image";
import { Upload, X } from "lucide-react";

type ImageUploaderProps = {
  productId: string;
  onUploadSuccess?: (imageBase64: string, mediaType: string) => void;
};

type StoredImage = { id: string; imageBase64: string; mediaType: string; sortOrder: number };
type ProductImagesResponse = { images?: StoredImage[]; imageBase64?: string | null; imageMediaType?: string | null };

export function ImageUploader({ productId, onUploadSuccess }: ImageUploaderProps) {
  const [isDragging, setIsDragging] = useState(false);
  const [isUploading, setIsUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [previews, setPreviews] = useState<{ name: string; url: string }[]>([]);
  const [storedImages, setStoredImages] = useState<StoredImage[]>([]);

  useEffect(() => {
    void fetch(`/api/products/${productId}`, { cache: "no-store" })
      .then((response) => response.ok ? response.json() as Promise<ProductImagesResponse> : null)
      .then((product) => {
        if (!product) return;
        if (product.images?.length) {
          setStoredImages(product.images);
        } else if (product.imageBase64) {
          setStoredImages([{ id: "primary", imageBase64: product.imageBase64, mediaType: product.imageMediaType ?? "image/jpeg", sortOrder: 0 }]);
        }
      });
  }, [productId]);

  const handleFiles = async (files: File[]) => {
    setError(null);

    const maxSize = 5 * 1024 * 1024;
    if (files.some((file) => file.size > maxSize)) {
      setError("Cada imagen no puede exceder 5 MB.");
      return;
    }

    const allowedTypes = ["image/jpeg", "image/png", "image/webp"];
    if (files.some((file) => !allowedTypes.includes(file.type))) {
      setError("Solo se permiten imágenes JPEG, PNG o WebP.");
      return;
    }

    setIsUploading(true);
    try {
      const formData = new FormData();
      files.forEach((file) => formData.append("files", file));

      const response = await fetch(`/api/products/${productId}/images`, {
        method: "POST",
        body: formData,
      });

      if (!response.ok) {
        const data = await response.json().catch(() => null);
        setError(data?.error ?? "Error al subir la imagen.");
        return;
      }

      setPreviews((current) => [...current, ...files.map((file) => ({ name: file.name, url: URL.createObjectURL(file) }))]);
    } catch {
      setError("No se pudo conectar con el servidor. Verificá que el API esté ejecutándose.");
    } finally {
      setIsUploading(false);
    }
  };

  const deleteStoredImage = async (imageId: string) => {
    if (imageId === "primary") {
      setStoredImages((current) => current.filter((image) => image.id !== imageId));
      return;
    }
    if (!window.confirm("¿Eliminar esta imagen del producto?")) return;
    const response = await fetch(`/api/products/${productId}/images?imageId=${encodeURIComponent(imageId)}`, { method: "DELETE" });
    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.error ?? "No se pudo eliminar la imagen.");
      return;
    }
    setStoredImages((current) => current.filter((image) => image.id !== imageId));
  };

  const onDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(true);
  };

  const onDragLeave = () => {
    setIsDragging(false);
  };

  const onDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragging(false);

    const files = e.dataTransfer.files;
    if (files.length > 0) void handleFiles(Array.from(files));
  };

  const onFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = e.currentTarget.files;
    if (files && files.length > 0) void handleFiles(Array.from(files));
  };

  return (
    <div className="flex flex-col gap-3">
      <div
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
        className={`relative rounded-lg border-2 border-dashed transition-colors ${
          isDragging
            ? "border-primary bg-primary/5"
            : "border-border/50 hover:border-border"
        }`}
      >
        <input
          type="file"
          accept="image/jpeg,image/png,image/webp"
          multiple
          onChange={onFileSelect}
          disabled={isUploading}
          className="absolute inset-0 cursor-pointer opacity-0"
        />
        <div className="flex flex-col items-center justify-center gap-2 py-6 px-3 text-center">
          <Upload className="size-5 text-muted-foreground" />
          <div>
            <p className="text-xs font-medium">Arrastrá una o varias imágenes</p>
              <p className="text-xs text-muted-foreground">o hacé clic para seleccionarlas</p>
          </div>
        </div>
      </div>

      {(storedImages.length > 0 || previews.length > 0) && <div className="grid grid-cols-3 gap-2">
        {storedImages.map((image) => <div key={image.id} className="relative aspect-square overflow-hidden rounded-lg border border-border/50"><Image src={`data:${image.mediaType};base64,${image.imageBase64}`} alt="Imagen del producto" fill unoptimized className="object-cover" sizes="33vw" /><button type="button" onClick={() => void deleteStoredImage(image.id)} className="absolute right-1 top-1 rounded-md bg-destructive/80 p-1 text-white"><X className="size-3" /></button></div>)}
        {previews.map((preview) => <div key={`${preview.name}-${preview.url}`} className="relative aspect-square overflow-hidden rounded-lg border border-border/50"><Image src={preview.url} alt={preview.name} fill unoptimized className="object-cover" sizes="33vw" /><button type="button" onClick={() => setPreviews((current) => current.filter((item) => item.url !== preview.url))} className="absolute right-1 top-1 rounded-md bg-destructive/80 p-1 text-white"><X className="size-3" /></button></div>)}
      </div>}

      {error && <p className="text-xs text-destructive">{error}</p>}

      {isUploading && <p className="text-xs text-muted-foreground">Subiendo imagen...</p>}
    </div>
  );
}
