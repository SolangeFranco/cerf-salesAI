"use client";

import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CheckCircle2, Copy, KeyRound, MessageSquare, Phone, Plus, RefreshCw, Settings2, ShieldCheck, Unplug } from "lucide-react";

type ChannelAccount = {
  id: string;
  provider: string;
  externalAccountId: string;
  displayName: string;
  status?: string;
  isOnline?: boolean;
};
type StoreSettings = { name: string; logoUrl: string | null; deliveryBaseCostGs: number; deliveryIncludedDistanceKm: number; deliveryExtraCostPerKmGs: number; metropolitanDeliveryCostGs: number; interiorDeliveryCostGs: number; aiResponsesEnabled: boolean; bankName: string | null; bankBeneficiaryName: string | null; bankAccountNumber: string | null; bankDocumentNumber: string | null; bankAlias: string | null };
type QrSession = { id: string; sessionId: string; qr: string | null; status: string };

export default function SettingsPage() {
  const [channels, setChannels] = useState<ChannelAccount[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [disconnectingChannelId, setDisconnectingChannelId] = useState<string | null>(null);
  const [reconnectingChannelId, setReconnectingChannelId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [copiedField, setCopiedField] = useState<string | null>(null);

  const [displayName, setDisplayName] = useState("");
  const [phoneNumberId, setPhoneNumberId] = useState("");
  const [qrDisplayName, setQrDisplayName] = useState("");
  const [qrSession, setQrSession] = useState<QrSession | null>(null);
  const [storeSettings, setStoreSettings] = useState<StoreSettings | null>(null);
  const [storeName, setStoreName] = useState("");
  const [logoUrl, setLogoUrl] = useState("");
  const [deliveryBaseCostGs, setDeliveryBaseCostGs] = useState("10000");
  const [deliveryIncludedDistanceKm, setDeliveryIncludedDistanceKm] = useState("10");
  const [deliveryExtraCostPerKmGs, setDeliveryExtraCostPerKmGs] = useState("0");
  const [pauseAiGlobally, setPauseAiGlobally] = useState(false);
  const [bankName, setBankName] = useState("");
  const [bankBeneficiaryName, setBankBeneficiaryName] = useState("");
  const [bankAccountNumber, setBankAccountNumber] = useState("");
  const [bankDocumentNumber, setBankDocumentNumber] = useState("");
  const [bankAlias, setBankAlias] = useState("");
  const [isSavingStore, setIsSavingStore] = useState(false);

  const loadChannels = async () => {
    try {
      const response = await fetch("/api/channels/whatsapp", { cache: "no-store" });
      if (response.ok) {
        setChannels((await response.json()) as ChannelAccount[]);
      }
    } catch {
      // Ignore initial error
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    void loadChannels();
    void fetch("/api/tenants/settings", { cache: "no-store" })
      .then((response) => response.ok ? response.json() as Promise<StoreSettings> : null)
      .then((settings) => {
        if (!settings) return;
        setStoreSettings(settings);
        setStoreName(settings.name);
        setDisplayName((current) => current || settings.name);
        setQrDisplayName((current) => current || settings.name);
        setLogoUrl(settings.logoUrl ?? "");
        setDeliveryBaseCostGs(String(settings.deliveryBaseCostGs));
        setDeliveryIncludedDistanceKm(String(settings.deliveryIncludedDistanceKm));
        setDeliveryExtraCostPerKmGs(String(settings.deliveryExtraCostPerKmGs));
        setPauseAiGlobally(!settings.aiResponsesEnabled);
        setBankName(settings.bankName ?? "");
        setBankBeneficiaryName(settings.bankBeneficiaryName ?? "");
        setBankAccountNumber(settings.bankAccountNumber ?? "");
        setBankDocumentNumber(settings.bankDocumentNumber ?? "");
        setBankAlias(settings.bankAlias ?? "");
      });
  }, []);

  useEffect(() => {
    const linkedChannel = channels.find((channel) => channel.provider === "linked-device");
    if (!linkedChannel || qrSession) return;
    void fetch(`/api/channels/whatsapp/linked-device/${linkedChannel.id}`, { cache: "no-store" })
      .then((response) => response.ok ? response.json() as Promise<{ id: string; sessionId: string; status: string; qr: string | null }> : null)
      .then((data) => {
        if (!data) return;
        setQrSession({ id: data.id, sessionId: data.sessionId, qr: data.qr, status: data.status });
      });
  }, [channels, qrSession]);

  const saveStoreSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setIsSavingStore(true);
    setError(null);
    setSuccess(null);
    try {
      const response = await fetch("/api/tenants/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          name: storeName.trim(),
          logoUrl: logoUrl.trim() || null,
          deliveryBaseCostGs: Number(deliveryBaseCostGs),
          deliveryIncludedDistanceKm: Number(deliveryIncludedDistanceKm),
          deliveryExtraCostPerKmGs: Number(deliveryExtraCostPerKmGs),
          metropolitanDeliveryCostGs: Number(deliveryBaseCostGs),
          interiorDeliveryCostGs: Number(deliveryBaseCostGs),
          aiResponsesEnabled: !pauseAiGlobally,
          bankName: bankName.trim() || null,
          bankBeneficiaryName: bankBeneficiaryName.trim() || null,
          bankAccountNumber: bankAccountNumber.trim() || null,
          bankDocumentNumber: bankDocumentNumber.trim() || null,
          bankAlias: bankAlias.trim() || null,
        }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.error ?? "No se pudo guardar la configuración.");
      setStoreSettings(data as StoreSettings);
      setSuccess("Configuración de tienda guardada.");
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo guardar la configuración.");
    } finally {
      setIsSavingStore(false);
    }
  };

  const handleConnectMeta = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim() || !phoneNumberId.trim()) return;

    setIsSubmitting(true);
    setError(null);
    setSuccess(null);

    try {
      const response = await fetch("/api/channels/whatsapp/meta", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName: displayName.trim(), phoneNumberId: phoneNumberId.trim() }),
      });

      if (!response.ok) {
        const data = await response.json().catch(() => null);
        setError(data?.error ?? "No se pudo conectar el canal Meta.");
      } else {
        setSuccess("¡Canal Meta conectado correctamente!");
        setDisplayName("");
        setPhoneNumberId("");
        await loadChannels();
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Error inesperado.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleConnectQr = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!qrDisplayName.trim()) return;
    setIsSubmitting(true);
    setError(null);
    setSuccess(null);
    try {
      const response = await fetch("/api/channels/whatsapp/linked-device", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ displayName: qrDisplayName.trim() }),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.detail ?? data?.error ?? "No se pudo iniciar la conexión QR.");
      setQrSession({ id: data.id, sessionId: data.sessionId, qr: data.qr, status: data.status });
      setQrDisplayName("");
      setSuccess("Escaneá el código QR desde WhatsApp → Dispositivos vinculados.");
      await loadChannels();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Error inesperado.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleDisconnectQr = async (channel: ChannelAccount) => {
    if (!window.confirm(`¿Cambiar el número de ${channel.displayName}? El canal actual se conservará en el historial.`)) return;
    setDisconnectingChannelId(channel.id);
    setError(null);
    setSuccess(null);
    try {
      const response = await fetch(`/api/channels/whatsapp/linked-device/${channel.id}`, { method: "DELETE" });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.detail ?? data?.error ?? "No se pudo desvincular WhatsApp.");
      if (qrSession?.id === channel.id) setQrSession(null);
      setSuccess("Sesión de WhatsApp desvinculada. Ya podés vincular otro número.");
      await loadChannels();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo desvincular WhatsApp.");
    } finally {
      setDisconnectingChannelId(null);
    }
  };

  const handleDisconnectMeta = async (channel: ChannelAccount) => {
    if (!window.confirm(`¿Desconectar el canal Meta "${channel.displayName}"? Vas a poder conectar otro método de WhatsApp.`)) return;
    setDisconnectingChannelId(channel.id);
    setError(null);
    setSuccess(null);
    try {
      const response = await fetch(`/api/channels/whatsapp/meta/${channel.id}`, { method: "DELETE" });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.detail ?? data?.error ?? "No se pudo desconectar el canal Meta.");
      setSuccess("Canal Meta desconectado. Ya podés conectar otro método de WhatsApp.");
      await loadChannels();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo desconectar el canal Meta.");
    } finally {
      setDisconnectingChannelId(null);
    }
  };

  const handleReconnectQr = async (channel: ChannelAccount) => {
    setReconnectingChannelId(channel.id);
    setError(null);
    setSuccess(null);
    try {
      const response = await fetch(`/api/channels/whatsapp/linked-device/${channel.id}/reconnect`, { method: "POST" });
      const data = await response.json().catch(() => null);
      if (!response.ok) throw new Error(data?.detail ?? data?.error ?? "No se pudo iniciar la reconexión QR.");
      setQrSession({ id: data.id, sessionId: data.sessionId, qr: data.qr, status: data.status });
      setSuccess("Escaneá el QR para reconectar este mismo número. La configuración de tienda se conserva.");
      await loadChannels();
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudo iniciar la reconexión QR.");
    } finally {
      setReconnectingChannelId(null);
    }
  };

  const startNewQrConnection = () => {
    setQrSession(null);
    setQrDisplayName("");
    setError(null);
    setSuccess(null);
    document.getElementById("qrDisplayName")?.focus();
  };

  useEffect(() => {
    if (!qrSession || qrSession.status === "connected") return;
    const timer = window.setInterval(() => {
      void fetch(`/api/channels/whatsapp/linked-device/${qrSession.id}`, { cache: "no-store" })
        .then(async (response) => {
          if (response.status === 404) {
            setQrSession(null);
            return null;
          }
          return response.ok ? await response.json() as { status: string; qr: string | null } : null;
        })
        .then((data) => {
          if (!data) return;
          setQrSession((current) => current ? { ...current, status: data.status, qr: data.qr } : current);
          if (data.status === "connected") {
            setSuccess("WhatsApp conectado mediante QR.");
            void loadChannels();
          }
        });
    }, 2500);
    return () => window.clearInterval(timer);
  }, [qrSession]);

  const copyToClipboard = (text: string, field: string) => {
    navigator.clipboard.writeText(text);
    setCopiedField(field);
    setTimeout(() => setCopiedField(null), 2000);
  };

  // Only one real WhatsApp connection can be active at a time; the other connect form is
  // locked until the active one is disconnected, so conversations never split across channels.
  const activeMetaChannel = channels.find((channel) => channel.provider === "meta");
  const activeLinkedDeviceChannel = channels.find((channel) => channel.provider === "linked-device" && channel.isOnline === true);

  return (
    <div className="mx-auto flex max-w-[1400px] flex-col gap-6">
      <div className="flex items-end justify-between gap-4 px-1">
        <div>
          <div className="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-primary">
            <Settings2 className="size-4" /> Configuración de la tienda
          </div>
          <h1 className="font-heading text-3xl font-semibold tracking-tight">Canales de WhatsApp</h1>
          <p className="mt-2 text-sm text-muted-foreground">
            Conectá y administrá tus cuentas de WhatsApp Cloud API o mediante código QR.
          </p>
          <p className="mt-2 max-w-2xl text-xs leading-5 text-muted-foreground/80">
            Para empezar sólo necesitás el Phone Number ID. VANTA usa tu catálogo como contexto del agente; no tenés que escribir prompts para probarlo.
          </p>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-destructive/30 bg-destructive/5 px-4 py-3 text-sm font-medium text-destructive">
          {error}
        </div>
      )}

      {success && (
        <div className="rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-3 text-sm font-medium text-emerald-700 dark:text-emerald-300">
          {success}
        </div>
      )}

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="border-border/70 bg-card/85 shadow-sm">
          <CardHeader className="border-b border-border/70 pb-4">
            <CardTitle className="text-lg">Identidad y delivery</CardTitle>
            <CardDescription>Estos datos aparecen en tu catálogo y en las respuestas de envío del agente.</CardDescription>
          </CardHeader>
          <CardContent className="p-6">
            <form onSubmit={saveStoreSettings} className="flex flex-col gap-4">
              <div className="flex flex-col gap-2"><Label htmlFor="storeName">Nombre de la tienda</Label><Input id="storeName" value={storeName} onChange={(event) => setStoreName(event.target.value)} placeholder="Ej. Tienda Aurora" required /></div>
              <div className="flex flex-col gap-2"><Label htmlFor="logoUrl">Logo URL <span className="font-normal text-muted-foreground">(opcional)</span></Label><Input id="logoUrl" type="url" value={logoUrl} onChange={(event) => setLogoUrl(event.target.value)} placeholder="https://.../logo.png" /><p className="text-[11px] text-muted-foreground">Usá una URL pública HTTPS. La carga de archivos se agregará cuando exista object storage.</p></div>
              <div className="grid gap-4 sm:grid-cols-2"><div className="flex flex-col gap-2"><Label htmlFor="deliveryBaseCost">Precio base (Gs.)</Label><Input id="deliveryBaseCost" type="number" min="0" step="1" value={deliveryBaseCostGs} onChange={(event) => setDeliveryBaseCostGs(event.target.value)} required /></div><div className="flex flex-col gap-2"><Label htmlFor="deliveryIncludedDistance">Distancia incluida (km)</Label><Input id="deliveryIncludedDistance" type="number" min="0.01" step="0.01" value={deliveryIncludedDistanceKm} onChange={(event) => setDeliveryIncludedDistanceKm(event.target.value)} required /></div></div>
              <div className="flex flex-col gap-2"><Label htmlFor="deliveryExtraCost">Precio por cada km extra (Gs.)</Label><Input id="deliveryExtraCost" type="number" min="0" step="1" value={deliveryExtraCostPerKmGs} onChange={(event) => setDeliveryExtraCostPerKmGs(event.target.value)} required /></div>
              <div className="rounded-xl border border-border/70 bg-background/50 p-4"><p className="text-sm font-semibold">Pagos por transferencia</p><p className="mt-1 text-xs text-muted-foreground">Estos datos se enviarán al cliente por WhatsApp cuando solicites el pago manual.</p><div className="mt-3 grid gap-3 sm:grid-cols-2"><Input aria-label="Banco" value={bankName} onChange={(event) => setBankName(event.target.value)} placeholder="Banco" /><Input aria-label="Nombre del beneficiario" value={bankBeneficiaryName} onChange={(event) => setBankBeneficiaryName(event.target.value)} placeholder="Nombre del beneficiario" /><Input aria-label="Número de cuenta" value={bankAccountNumber} onChange={(event) => setBankAccountNumber(event.target.value)} placeholder="Número de cuenta" /><Input aria-label="CI o RUC" value={bankDocumentNumber} onChange={(event) => setBankDocumentNumber(event.target.value)} placeholder="CI o RUC" /><Input aria-label="Alias" value={bankAlias} onChange={(event) => setBankAlias(event.target.value)} placeholder="Alias (opcional)" /></div></div>
              <label className="flex cursor-pointer items-start gap-3 rounded-xl border border-border/70 bg-background/50 p-4"><input type="checkbox" checked={pauseAiGlobally} onChange={(event) => setPauseAiGlobally(event.target.checked)} className="mt-0.5 size-4 accent-primary" /><span><span className="block text-sm font-semibold">Pausar IA en todos los chats</span><span className="mt-1 block text-xs leading-5 text-muted-foreground">Pausa los chats actuales y los nuevos. Podés activar la IA individualmente desde cada conversación.</span></span></label>
              <Button type="submit" disabled={isSavingStore || !storeName.trim()} className="mt-2 h-10 rounded-xl font-semibold">{isSavingStore ? "Guardando..." : "Guardar configuración"}</Button>
            </form>
          </CardContent>
        </Card>
        {/* Active channels */}
        <Card className="border-border/70 bg-card/85 shadow-sm">
          <CardHeader className="border-b border-border/70 pb-4">
            <div className="flex items-center justify-between">
              <div>
                <CardTitle className="text-lg">Canales conectados</CardTitle>
                <CardDescription>Cuentas activas en tu workspace. Podés desvincular cualquier sesión QR para conectar otro número.</CardDescription>
              </div>
              <Button variant="ghost" size="icon" onClick={() => void loadChannels()} disabled={isLoading}>
                <RefreshCw className={`size-4 ${isLoading ? "animate-spin" : ""}`} />
              </Button>
            </div>
          </CardHeader>

          <CardContent className="p-6">
            {isLoading && <p className="text-sm text-muted-foreground">Cargando canales...</p>}

            {!isLoading && channels.length === 0 && (
              <div className="flex min-h-[180px] flex-col items-center justify-center text-center text-muted-foreground">
                <MessageSquare className="mb-3 size-8" />
                <p className="text-sm">No hay canales de WhatsApp conectados aún.</p>
                <p className="mt-1 text-xs">Conectá tu Phone Number ID de Meta o escaneá un código QR para empezar.</p>
              </div>
            )}

            <div className={`mb-4 flex items-center justify-between rounded-2xl border p-4 ${channels.some((channel) => channel.isOnline) ? "border-emerald-500/30 bg-emerald-500/5" : "border-amber-500/30 bg-amber-500/5"}`}><div><p className="text-sm font-semibold">Perfil administrador</p><p className="text-xs text-muted-foreground">Sesión del número conectado</p></div><Badge variant="outline" className={channels.some((channel) => channel.isOnline) ? "border-emerald-500/30 text-emerald-700" : "border-amber-500/30 text-amber-700"}>{channels.some((channel) => channel.isOnline) ? "Conectado" : "Desconectado"}</Badge></div>
            <div className="flex flex-col gap-3">
              {channels.map((channel) => {
                const isLinkedDeviceDisconnected = channel.provider === "linked-device" && !channel.isOnline;

                return (
                  <div
                    key={channel.id}
                    className={`flex flex-wrap items-center justify-between gap-4 rounded-xl border p-4 transition-all hover:bg-muted/40 ${isLinkedDeviceDisconnected ? "border-amber-500/40 bg-amber-500/5" : "border-border/70 bg-background/50"}`}
                  >
                    <div className="flex min-w-0 items-center gap-3">
                      <div className={`flex size-10 items-center justify-center rounded-full ${isLinkedDeviceDisconnected ? "bg-amber-500/15 text-amber-700" : "bg-[#25d366]/15 text-[#159447]"}`}>
                        <Phone className="size-5" />
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-semibold">{channel.displayName}</p>
                        <p className="text-xs text-muted-foreground">
                          ID: <code className="rounded bg-muted px-1.5 py-0.5">{channel.externalAccountId}</code>
                        </p>
                        {isLinkedDeviceDisconnected && (
                          <p className="mt-1 text-[11px] font-medium text-amber-700 dark:text-amber-300">
                            WhatsApp desconectado. Reconectá el número para seguir recibiendo mensajes.
                          </p>
                        )}
                      </div>
                    </div>

                    <div className="flex flex-wrap items-center justify-end gap-2">
                      <Badge
                        variant="outline"
                        className={isLinkedDeviceDisconnected ? "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300" : "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400"}
                      >
                        {channel.provider === "meta" ? "WhatsApp Cloud API" : channel.provider === "linked-device" ? `WhatsApp por QR · ${channel.isOnline ? "Conectado" : "Desconectado"}` : "Mock Adapter"}
                      </Badge>
                      {channel.provider === "meta" && (
                        <Button
                          type="button"
                          variant="secondary"
                          size="sm"
                          className="h-8 rounded-lg"
                          onClick={() => void handleDisconnectMeta(channel)}
                          disabled={disconnectingChannelId === channel.id}
                        >
                          <Unplug className="mr-1.5 size-3.5" />
                          {disconnectingChannelId === channel.id ? "Desconectando..." : "Desconectar"}
                        </Button>
                      )}
                      {channel.provider === "linked-device" && (
                        <>
                          <Button
                            type="button"
                            variant={isLinkedDeviceDisconnected ? "default" : "outline"}
                            size="sm"
                            className={isLinkedDeviceDisconnected ? "h-8 rounded-lg bg-amber-600 text-white hover:bg-amber-700" : "h-8 rounded-lg"}
                            onClick={() => void handleReconnectQr(channel)}
                            disabled={reconnectingChannelId === channel.id || disconnectingChannelId === channel.id}
                          >
                            <Unplug className="mr-1.5 size-3.5" />
                            {reconnectingChannelId === channel.id ? "Reconectando..." : isLinkedDeviceDisconnected ? "Reconectar con QR" : "Reconectar"}
                          </Button>
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            className="h-8 rounded-lg"
                            onClick={() => void handleDisconnectQr(channel)}
                            disabled={reconnectingChannelId === channel.id || disconnectingChannelId === channel.id}
                          >
                            <Plus className="mr-1.5 size-3.5" />
                            Cambiar número
                          </Button>
                        </>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          </CardContent>
        </Card>

        {/* Connect new Meta channel */}
        <Card className="border-border/70 bg-card/85 shadow-sm">
          <CardHeader className="border-b border-border/70 pb-4">
            <CardTitle className="text-lg">Conectar nuevo canal de Meta</CardTitle>
            <CardDescription>Ingresá los identificadores de tu App de Meta</CardDescription>
          </CardHeader>

          <CardContent className="p-6">
            {activeLinkedDeviceChannel ? (
              <div className="flex flex-col gap-3 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm">
                <p className="font-medium text-amber-800 dark:text-amber-300">Ya tenés WhatsApp conectado por QR: {activeLinkedDeviceChannel.displayName}. Desconectalo para usar Cloud API y evitar dividir tus conversaciones.</p>
                <Button type="button" variant="outline" size="sm" className="self-start rounded-lg" onClick={() => void handleDisconnectQr(activeLinkedDeviceChannel)} disabled={disconnectingChannelId === activeLinkedDeviceChannel.id}>
                  <Unplug className="mr-1.5 size-3.5" />
                  {disconnectingChannelId === activeLinkedDeviceChannel.id ? "Desconectando..." : "Desconectar QR"}
                </Button>
              </div>
            ) : (
            <form onSubmit={handleConnectMeta} className="flex flex-col gap-4">
              <div className="flex flex-col gap-2">
                <Label htmlFor="displayName" className="text-xs font-semibold">
                  Nombre descriptivo
                </Label>
                <Input
                  id="displayName"
                  placeholder="Ej. Tienda Oficial Asunción"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  className="h-10 rounded-xl"
                  required
                />
              </div>

              <div className="flex flex-col gap-2">
                <Label htmlFor="phoneNumberId" className="text-xs font-semibold">
                  Phone Number ID
                </Label>
                <Input
                  id="phoneNumberId"
                  placeholder="ID provisto por Meta"
                  value={phoneNumberId}
                  onChange={(e) => setPhoneNumberId(e.target.value)}
                  className="h-10 rounded-xl"
                  required
                />
                <p className="text-[11px] text-muted-foreground">
                  Lo encontrás en Meta for Developers → WhatsApp → Configuración de la API.
                </p>
              </div>

              <Button
                type="submit"
                disabled={isSubmitting || !displayName.trim() || !phoneNumberId.trim()}
                className="mt-2 h-10 rounded-xl bg-primary text-primary-foreground font-semibold"
              >
                <Plus className="mr-2 size-4" />
                {isSubmitting ? "Conectando..." : "Conectar canal Meta"}
              </Button>
            </form>
            )}
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="border-border/70 bg-card/85 shadow-sm">
          <CardHeader className="border-b border-border/70 pb-4">
            <div className="flex items-start justify-between gap-4">
              <div>
                <CardTitle className="text-lg">Conectar nuevo número por QR</CardTitle>
                <CardDescription>Usá un número personal o WhatsApp Business mediante un dispositivo vinculado.</CardDescription>
              </div>
              <Button type="button" variant="outline" size="sm" className="shrink-0 rounded-lg" onClick={startNewQrConnection}><Plus className="mr-1.5 size-3.5" />Nuevo número</Button>
            </div>
          </CardHeader>
          <CardContent className="p-6">
            {activeMetaChannel ? (
              <div className="flex flex-col gap-3 rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-sm">
                <p className="font-medium text-amber-800 dark:text-amber-300">Ya tenés un canal Meta Cloud API activo: {activeMetaChannel.displayName}. Desconectalo para usar QR y evitar dividir tus conversaciones.</p>
                <Button type="button" variant="outline" size="sm" className="self-start rounded-lg" onClick={() => void handleDisconnectMeta(activeMetaChannel)} disabled={disconnectingChannelId === activeMetaChannel.id}>
                  <Unplug className="mr-1.5 size-3.5" />
                  {disconnectingChannelId === activeMetaChannel.id ? "Desconectando..." : "Desconectar Meta"}
                </Button>
              </div>
            ) : (
            <form onSubmit={handleConnectQr} className="flex flex-col gap-4">
              <div className="flex flex-col gap-2">
                <Label htmlFor="qrDisplayName" className="text-xs font-semibold">Nombre descriptivo</Label>
                <Input id="qrDisplayName" placeholder="Ej. WhatsApp de ventas" value={qrDisplayName} onChange={(e) => setQrDisplayName(e.target.value)} className="h-10 rounded-xl" required />
              </div>
              <p className="text-[11px] leading-5 text-muted-foreground">La conexión QR depende de WhatsApp Web y puede desconectarse o quedar limitada por Meta. No es la API oficial de Cloud.</p>
              <Button type="submit" disabled={isSubmitting || !qrDisplayName.trim()} className="mt-2 h-10 rounded-xl font-semibold"><Phone className="mr-2 size-4" />{isSubmitting ? "Iniciando..." : "Mostrar código QR"}</Button>
            </form>
            )}
            {qrSession && <div className="mt-5 flex flex-col items-center gap-3 rounded-xl border border-border/70 bg-background/60 p-4 text-center"><p className="text-xs font-semibold">Estado: {qrSession.status === "connected" ? "conectado" : "esperando escaneo"}</p>{qrSession.qr && <img src={qrSession.qr} alt="Código QR para vincular WhatsApp" className="size-56" />}</div>}
          </CardContent>
        </Card>
      </div>

      {/* Webhook credentials guide */}
      <Card className="border-border/70 bg-card/85 shadow-sm">
        <CardHeader className="border-b border-border/70 pb-4">
          <div className="flex items-center gap-2">
            <ShieldCheck className="size-5 text-primary" />
            <CardTitle className="text-lg">Datos para el Webhook de Meta</CardTitle>
          </div>
          <CardDescription>Configurá estos valores en tu consola de Meta for Developers</CardDescription>
        </CardHeader>

        <CardContent className="grid gap-4 p-6 sm:grid-cols-2">
          <div className="flex flex-col gap-2 rounded-xl border border-border/70 bg-background/50 p-4">
            <span className="text-xs font-semibold text-muted-foreground">Verify Token (Token de Verificación)</span>
            <div className="flex items-center justify-between gap-2">
              <code className="text-sm font-bold text-foreground">vanta_verify_token_2026</code>
              <Button
                variant="ghost"
                size="sm"
                className="h-8 px-2"
                onClick={() => copyToClipboard("vanta_verify_token_2026", "token")}
              >
                {copiedField === "token" ? <CheckCircle2 className="size-4 text-emerald-600" /> : <Copy className="size-4" />}
              </Button>
            </div>
          </div>

          <div className="flex flex-col gap-2 rounded-xl border border-border/70 bg-background/50 p-4">
            <span className="text-xs font-semibold text-muted-foreground">Suscripción requerida</span>
            <div className="flex items-center gap-2">
              <KeyRound className="size-4 text-primary" />
              <span className="text-sm font-semibold text-foreground">messages (Eventos de mensajes)</span>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
