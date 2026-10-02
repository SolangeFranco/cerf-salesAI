"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { ConversationMessageBubble } from "@/components/dashboard/conversation-message-bubble";

type Message = { id: string; direction: string; text: string; imageBase64: string | null; imageMediaType: string | null; deliveryStatus?: string | null; createdAt: string };
type Product = { id: string; name: string; sku: string; price: number };

const currency = new Intl.NumberFormat("es-PY", { style: "currency", currency: "PYG", maximumFractionDigits: 0 });

export default function ConversationDetailPage({ params }: { params: Promise<{ conversationId: string }> }) {
  const [conversationId, setConversationId] = useState("");
  const [messages, setMessages] = useState<Message[]>([]);
  const [query, setQuery] = useState("");
  const [products, setProducts] = useState<Product[]>([]);
  const [messageText, setMessageText] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [isAgentResponding, setIsAgentResponding] = useState(false);
  const [agentResult, setAgentResult] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    params.then(({ conversationId: id }) => {
      setConversationId(id);
      return fetch(`/api/conversations/${id}/messages`, { cache: "no-store" });
    }).then(async (response) => {
      if (!response.ok) throw new Error("No se pudieron cargar los mensajes.");
      setMessages((await response.json()) as Message[]);
    }).catch((reason: Error) => setError(reason.message)).finally(() => setIsLoading(false));
  }, [params]);

  const searchProducts = async () => {
    if (query.trim().length < 2) return;
    setError(null);
    const response = await fetch(`/api/ai/tools/search-products?query=${encodeURIComponent(query.trim())}`, { cache: "no-store" });
    if (!response.ok) {
      setError("No se pudo buscar en el catálogo.");
      return;
    }
    setProducts((await response.json()) as Product[]);
  };

  const sendMessage = async () => {
    if (!messageText.trim() || !conversationId) return;
    setIsSending(true);
    setError(null);
    const response = await fetch(`/api/conversations/${conversationId}/messages/send`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text: messageText, idempotencyKey: crypto.randomUUID() }),
    });
    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setError(data?.error ?? "No se pudo enviar el mensaje.");
    } else {
      setMessageText("");
      const refreshed = await fetch(`/api/conversations/${conversationId}/messages`, { cache: "no-store" });
      if (refreshed.ok) setMessages((await refreshed.json()) as Message[]);
    }
    setIsSending(false);
  };

  const respondWithAgent = async () => {
    if (!conversationId) return;
    setIsAgentResponding(true);
    setAgentResult(null);
    setError(null);
    const response = await fetch(`/api/conversations/${conversationId}/agent/mock-respond`, { method: "POST" });
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      setError(data?.error ?? "No se pudo ejecutar el agente.");
    } else {
      setAgentResult(data?.text ?? "El agente procesó la conversación.");
      const refreshed = await fetch(`/api/conversations/${conversationId}/messages`, { cache: "no-store" });
      if (refreshed.ok) setMessages((await refreshed.json()) as Message[]);
    }
    setIsAgentResponding(false);
  };

  return (
    <div className="flex flex-col gap-8">
      <div><Link href="/dashboard/conversations" className="text-sm text-muted-foreground hover:text-foreground">Volver a conversaciones</Link><h1 className="mt-2 text-2xl font-semibold tracking-tight">Conversación</h1><p className="text-sm text-muted-foreground">{conversationId}</p></div>
      {error && <p className="text-sm text-destructive">{error}</p>}
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_360px]">
        <Card><CardHeader><CardTitle className="text-lg">Mensajes</CardTitle><CardDescription>Conversación comercial con el cliente.</CardDescription></CardHeader><CardContent><div className="flex flex-col gap-3">{isAgentResponding && <p className="rounded-md border bg-muted/40 px-3 py-2 text-sm text-muted-foreground">Agente mock buscando en el catálogo...</p>}{agentResult && <p className="rounded-md border border-emerald-600/30 bg-emerald-50 px-3 py-2 text-sm text-emerald-900">{agentResult}</p>}{isLoading && <p className="text-sm text-muted-foreground">Cargando mensajes...</p>}{!isLoading && messages.length === 0 && <p className="text-sm text-muted-foreground">No hay mensajes.</p>}{messages.map((message) => <ConversationMessageBubble key={message.id} message={message} compact />)}<div className="flex gap-2 border-t pt-4"><Input value={messageText} onChange={(event) => setMessageText(event.target.value)} placeholder="Responder..." onKeyDown={(event) => { if (event.key === "Enter") void sendMessage(); }} /><button type="button" disabled={isSending || !messageText.trim()} onClick={() => void sendMessage()} className="rounded-lg bg-primary px-3 text-sm font-medium text-primary-foreground disabled:opacity-50">{isSending ? "Enviando..." : "Enviar"}</button></div><button type="button" disabled={isAgentResponding} onClick={() => void respondWithAgent()} className="rounded-lg border px-3 py-2 text-sm font-medium hover:bg-muted disabled:opacity-50">{isAgentResponding ? "Procesando..." : "Responder con agente mock"}</button></div></CardContent></Card>
        <Card><CardHeader><CardTitle className="text-lg">Buscar productos</CardTitle><CardDescription>Tool controlado: solo catálogo activo de tu tenant.</CardDescription></CardHeader><CardContent className="flex flex-col gap-4"><div className="flex gap-2"><Input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="camisa, zapatos..." onKeyDown={(event) => { if (event.key === "Enter") void searchProducts(); }} /><button type="button" onClick={() => void searchProducts()} className="rounded-lg border px-3 text-sm font-medium hover:bg-muted">Buscar</button></div>{products.map((product) => <div key={product.id} className="flex justify-between gap-3 border-b pb-3 text-sm"><div><p className="font-medium">{product.name}</p><p className="text-muted-foreground">{product.sku}</p></div><p>{currency.format(product.price)}</p></div>)}</CardContent></Card>
      </div>
    </div>
  );
}
