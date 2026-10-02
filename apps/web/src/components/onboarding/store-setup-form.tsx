"use client";

import { useState } from "react";
import { ArrowRight, Image, LoaderCircle, Store } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type Props = { tenantName: string; onComplete: () => void };

export function StoreSetupForm({ tenantName, onComplete }: Props) {
  const [storeName, setStoreName] = useState(tenantName);
  const [logoUrl, setLogoUrl] = useState("");
  const [baseCostGs, setBaseCostGs] = useState("");
  const [includedDistanceKm, setIncludedDistanceKm] = useState("");
  const [extraCostPerKmGs, setExtraCostPerKmGs] = useState("");
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const saveSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setIsSaving(true);
    setError(null);
    try {
      const baseCost = Number(baseCostGs);
      const response = await fetch("/api/tenants/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: storeName.trim(),
          logoUrl: logoUrl.trim() || null,
          deliveryBaseCostGs: baseCost,
          deliveryIncludedDistanceKm: Number(includedDistanceKm),
          deliveryExtraCostPerKmGs: Number(extraCostPerKmGs),
          metropolitanDeliveryCostGs: baseCost,
          interiorDeliveryCostGs: baseCost,
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error ?? data?.detail ?? "No se pudo guardar la configuración.");
      onComplete();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo guardar la configuración.");
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="w-full max-w-3xl">
      <div className="mb-10 max-w-2xl">
        <p className="mb-3 text-xs font-bold uppercase tracking-[0.22em] text-[#159447]">Siguiente paso</p>
        <h1 className="text-4xl font-black leading-[0.95] tracking-[-0.06em] text-[#202526] sm:text-6xl">Configurá tu tienda.</h1>
        <p className="mt-5 max-w-xl text-sm leading-6 text-[#65736d]">Estos datos van a aparecer en tu catálogo y ayudan al agente a responder consultas de delivery con información real.</p>
      </div>

      {error && <div role="alert" className="mb-6 rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-semibold text-red-700">{error}</div>}

      <form onSubmit={saveSettings} className="rounded-[24px] border border-[#d6e1d8] bg-white p-6 shadow-[0_18px_45px_rgba(23,60,55,0.08)] sm:p-8">
        <div className="grid gap-6 sm:grid-cols-2">
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="setup-store-name">Nombre de la tienda</Label>
            <div className="relative"><Store className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[#889893]" /><Input id="setup-store-name" value={storeName} onChange={(event) => setStoreName(event.target.value)} className="h-11 rounded-xl pl-10" placeholder="Ej. Tienda Aurora" required /></div>
          </div>
          <div className="flex flex-col gap-2 sm:col-span-2">
            <Label htmlFor="setup-logo-url">Logo de la tienda <span className="font-normal text-[#889893]">(opcional)</span></Label>
            <div className="relative"><Image className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-[#889893]" /><Input id="setup-logo-url" type="url" value={logoUrl} onChange={(event) => setLogoUrl(event.target.value)} className="h-11 rounded-xl pl-10" placeholder="https://.../logo.png" /><span className="text-[11px] leading-4 text-[#889893]">Usá una URL pública HTTPS.</span></div>
          </div>
          <div className="sm:col-span-2"><h2 className="text-lg font-black tracking-[-0.03em] text-[#202526]">Política de delivery</h2><p className="mt-1 text-sm leading-5 text-[#65736d]">Definí el precio base, la distancia incluida y el adicional por cada kilómetro extra.</p></div>
          <div className="flex flex-col gap-2"><Label htmlFor="setup-base-cost">Precio base (Gs.)</Label><Input id="setup-base-cost" type="number" min="0" step="1" value={baseCostGs} onChange={(event) => setBaseCostGs(event.target.value)} className="h-11 rounded-xl" required /></div>
          <div className="flex flex-col gap-2"><Label htmlFor="setup-included-distance">Distancia incluida (km)</Label><Input id="setup-included-distance" type="number" min="0.01" step="0.01" value={includedDistanceKm} onChange={(event) => setIncludedDistanceKm(event.target.value)} className="h-11 rounded-xl" required /></div>
          <div className="flex flex-col gap-2 sm:col-span-2"><Label htmlFor="setup-extra-cost">Precio por cada km extra (Gs.)</Label><Input id="setup-extra-cost" type="number" min="0" step="1" value={extraCostPerKmGs} onChange={(event) => setExtraCostPerKmGs(event.target.value)} className="h-11 rounded-xl" required /></div>
        </div>
        <Button type="submit" disabled={isSaving || !storeName.trim()} className="mt-8 h-11 w-full rounded-xl bg-[#159447] font-bold text-white hover:bg-[#117b3a] sm:w-auto sm:px-8">{isSaving ? <LoaderCircle className="size-4 animate-spin" /> : <ArrowRight className="size-4" />} Guardar y continuar</Button>
      </form>
    </div>
  );
}