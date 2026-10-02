"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowRight, CheckCircle2, Cloud, LoaderCircle, QrCode } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { StoreSetupForm } from "@/components/onboarding/store-setup-form";

type QrSession = { channelId: string; qr: string | null; status: string };

type Props = { tenantName: string; hasWhatsAppChannel: boolean };

export function WhatsAppConnectionOptions({ tenantName, hasWhatsAppChannel }: Props) {
  const router = useRouter();
  const [displayName, setDisplayName] = useState(`${tenantName} WhatsApp`);
  const [phoneNumberId, setPhoneNumberId] = useState("");
  const [qrSession, setQrSession] = useState<QrSession | null>(null);
  const [isSubmitting, setIsSubmitting] = useState<"meta" | "qr" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isStoreSetup, setIsStoreSetup] = useState(hasWhatsAppChannel);
  const stagingDemoEnabled = process.env.NEXT_PUBLIC_ENABLE_STAGING_DEMO === "true";

  useEffect(() => {
    if (!qrSession || qrSession.status === "connected") return;
    const timer = window.setInterval(() => {
      void fetch(`/api/channels/whatsapp/linked-device/${qrSession.channelId}`, { cache: "no-store" })
        .then(async (response) => {
          if (response.status === 404) {
            setQrSession(null);
            setError("La sesión QR ya no está disponible. Generá un nuevo código para continuar.");
            return null;
          }
          return response.ok ? await response.json() as { status: string; qr: string | null } : null;
        })
        .then((data) => {
          if (!data) return;
          setQrSession((current) => current ? { ...current, status: data.status, qr: data.qr } : current);
          if (data.status === "connected") window.setTimeout(() => setIsStoreSetup(true), 450);
        });
    }, 2500);
    return () => window.clearInterval(timer);
  }, [qrSession?.channelId, qrSession?.status]);

  const connectMeta = async (event: React.FormEvent) => {
    event.preventDefault();
    setIsSubmitting("meta");
    setError(null);
    try {
      const response = await fetch("/api/channels/whatsapp/meta", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName: displayName.trim(), phoneNumberId: phoneNumberId.trim() }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error ?? "No se pudo conectar WhatsApp Cloud API.");
      setIsStoreSetup(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo conectar el canal.");
    } finally {
      setIsSubmitting(null);
    }
  };

  const connectQr = async () => {
    setIsSubmitting("qr");
    setError(null);
    try {
      const response = await fetch("/api/channels/whatsapp/linked-device", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName: displayName.trim() }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.detail ?? data?.error ?? "No se pudo iniciar la conexión QR.");
      setQrSession({ channelId: data.id, qr: data.qr, status: data.status });
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo iniciar la conexión QR.");
    } finally {
      setIsSubmitting(null);
    }
  };

  const connectDemo = async () => {
    setIsSubmitting("qr");
    setError(null);
    try {
      const response = await fetch("/api/channels/whatsapp/mock", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          displayName: `${tenantName} Canal de prueba`,
          externalAccountId: `mock:staging-${crypto.randomUUID()}`,
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error ?? "No se pudo crear el canal de prueba.");
      setIsStoreSetup(true);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo crear el canal de prueba.");
    } finally {
      setIsSubmitting(null);
    }
  };

  const qrConnected = qrSession?.status === "connected";

  if (isStoreSetup) return <StoreSetupForm tenantName={tenantName} onComplete={() => { router.push("/dashboard"); router.refresh(); }} />;

  return (
    <div className="w-full max-w-5xl">
      <div className="mb-10 max-w-2xl">
        <p className="mb-3 text-xs font-bold uppercase tracking-[0.22em] text-[#159447]">{hasWhatsAppChannel ? "Último paso" : "Primer paso"}</p>
        <h1 className="text-4xl font-black leading-[0.95] tracking-[-0.06em] text-[#202526] sm:text-6xl">{hasWhatsAppChannel ? "Configurá tu tienda." : "Conectá tu WhatsApp."}</h1>
        <p className="mt-5 max-w-xl text-sm leading-6 text-[#65736d]">{hasWhatsAppChannel ? "Tu WhatsApp ya está conectado. Completá los datos de tu tienda para empezar." : "Elegí cómo querés conectar el número que usás para vender. Podés cambiar de método más adelante desde Configuración."}</p>
      </div>

      {error && <div role="alert" className="mb-6 rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm font-semibold text-red-700">{error}</div>}

      {stagingDemoEnabled && <div className="mb-5 flex flex-col items-start justify-between gap-4 border-l-4 border-[#d2a05a] bg-[#fff8ed] px-5 py-4 sm:flex-row sm:items-center"><div><p className="text-sm font-black text-[#202526]">Ambiente de prueba</p><p className="mt-1 text-xs leading-5 text-[#65736d]">Creá un canal sintético para recorrer el producto sin conectar un número real.</p></div><Button type="button" variant="outline" onClick={() => void connectDemo()} disabled={isSubmitting !== null} className="shrink-0 border-[#d2a05a] bg-white font-bold text-[#202526] hover:bg-[#fff3df]">{isSubmitting === "qr" ? <LoaderCircle className="size-4 animate-spin" /> : <ArrowRight className="size-4" />} Continuar con canal de prueba</Button></div>}

      <div className="grid gap-5 lg:grid-cols-2">
        <form onSubmit={connectMeta} className="flex flex-col rounded-[24px] border border-[#d6e1d8] bg-white p-6 shadow-[0_18px_45px_rgba(23,60,55,0.08)] sm:p-8">
          <div className="flex size-12 items-center justify-center rounded-2xl bg-[#e8f5eb] text-[#159447]"><Cloud className="size-6" /></div>
          <h2 className="mt-6 text-2xl font-black tracking-[-0.04em] text-[#202526]">WhatsApp Cloud API</h2>
          <p className="mt-2 min-h-12 text-sm leading-5 text-[#65736d]">La integración oficial de Meta, ideal para operación estable, varios agentes y mayor control.</p>
          <div className="mt-6 flex flex-col gap-2"><Label htmlFor="onboarding-display-name">Nombre del canal</Label><Input id="onboarding-display-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} className="h-11 rounded-xl" required /></div>
          <div className="mt-4 flex flex-col gap-2"><Label htmlFor="onboarding-phone-number-id">Phone Number ID</Label><Input id="onboarding-phone-number-id" placeholder="ID provisto por Meta" value={phoneNumberId} onChange={(event) => setPhoneNumberId(event.target.value)} className="h-11 rounded-xl" required /><span className="text-[11px] leading-4 text-[#889893]">Lo encontrás en Meta for Developers → WhatsApp → Configuración de la API.</span></div>
          <Button type="submit" disabled={isSubmitting !== null || !phoneNumberId.trim()} className="mt-7 h-11 rounded-xl bg-[#159447] font-bold text-white hover:bg-[#117b3a]">{isSubmitting === "meta" ? <LoaderCircle className="size-4 animate-spin" /> : <ArrowRight className="size-4" />} Conectar con Meta</Button>
        </form>

        <div className="flex flex-col rounded-[24px] border border-[#d7dce4] bg-[#292d32] p-6 text-white shadow-[0_18px_45px_rgba(41,45,50,0.16)] sm:p-8">
          <div className="flex size-12 items-center justify-center rounded-2xl bg-[#25d366] text-[#173c37]"><QrCode className="size-6" /></div>
          <h2 className="mt-6 text-2xl font-black tracking-[-0.04em]">Conexión mediante QR</h2>
          <p className="mt-2 min-h-12 text-sm leading-5 text-[#c1ccc8]">Vinculá tu número personal o WhatsApp Business como un dispositivo conectado, sin configurar Meta Business.</p>
          {!qrSession && <Button type="button" onClick={() => void connectQr()} disabled={isSubmitting !== null} className="mt-7 h-11 rounded-xl bg-[#25d366] font-bold text-[#173c37] hover:bg-[#5bea88]">{isSubmitting === "qr" ? <LoaderCircle className="size-4 animate-spin" /> : <QrCode className="size-4" />} Generar código QR</Button>}
          {qrSession && <div className="mt-6 flex flex-1 flex-col items-center justify-center gap-4 rounded-2xl border border-white/10 bg-white p-5 text-center text-[#202526]"><p className="text-sm font-bold">{qrConnected ? "WhatsApp conectado" : "Escaneá este código desde WhatsApp"}</p>{qrSession.qr && <div className="relative size-56"><img src={qrSession.qr} alt="Código QR para vincular WhatsApp" className="size-full" /><span className="absolute left-1/2 top-1/2 flex size-14 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-2xl border-4 border-white bg-white shadow-md"><img src="/images/whatsapp-brands-solid-full.svg" alt="WhatsApp" className="size-8" /></span></div>}{qrConnected && <CheckCircle2 className="size-7 text-[#159447]" />}</div>}
          <p className="mt-5 text-[11px] leading-4 text-[#aebbb6]">Esta opción depende de WhatsApp Web y puede desconectarse o quedar limitada por Meta. No es la API oficial de Cloud.</p>
        </div>
      </div>
    </div>
  );
}
