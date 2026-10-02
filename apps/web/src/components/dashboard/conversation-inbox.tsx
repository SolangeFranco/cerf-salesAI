"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, Edit3, Filter, MessageCircle, Pause, Phone, Play, Reply, Search, Send, ShieldCheck, Sparkles, Video, X } from "lucide-react";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { ConversationMessageBubble } from "@/components/dashboard/conversation-message-bubble";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";

type Conversation = {
  id: string;
  customerExternalId: string;
  customerName: string;
  customerImageUrl: string | null;
  isGroup: boolean;
  isChannel: boolean;
  status: string;
  currentIntent: string | null;
  assignedUserId: string | null;
  lastMessageAt: string;
  lastActivityAt: string;
};
type ConversationPage = { conversations: Conversation[]; hasMore: boolean };
type Message = { id: string; direction: string; text: string; audioTranscript?: string | null; imageBase64: string | null; imageMediaType: string | null; mediaBase64?: string | null; mediaMimeType?: string | null; mediaFileName?: string | null; mediaKind?: string | null; mediaUnavailable?: boolean; quotedExternalMessageId?: string | null; quotedText?: string | null; isDeleted?: boolean; deliveryStatus?: string | null; reactionsJson?: string | null; createdAt: string };
type MessagePage = { messages: Message[]; hasMore: boolean };
type ConversationContext = {
  cart: { id: string; total: number; items: { productName: string; quantity: number; unitPrice: number }[] } | null;
  latestOrder: { id: string; number: string; status: string; total: number; createdAt: string } | null;
};

const STATUS_LABELS: Record<string, string> = {
  AI_ACTIVE: "IA activa",
  HUMAN_ACTIVE: "Atendido por vos",
  PAUSED: "Pausado",
  CLOSED: "Cerrado",
};

function isSyntheticDirectConversation(conversation: Conversation) {
  return !conversation.isGroup && !conversation.isChannel && conversation.customerName === conversation.customerExternalId && /^\d+$/.test(conversation.customerExternalId);
}

export function ConversationInbox() {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [context, setContext] = useState<ConversationContext | null>(null);
  const [search, setSearch] = useState("");
  const [draft, setDraft] = useState("");
  const [replyingTo, setReplyingTo] = useState<Message | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isLoadingMessages, setIsLoadingMessages] = useState(false);
  const [isLoadingOlderMessages, setIsLoadingOlderMessages] = useState(false);
  const [isLoadingOlderConversations, setIsLoadingOlderConversations] = useState(false);
  const [isSending, setIsSending] = useState(false);
  const [isAgentResponding, setIsAgentResponding] = useState(false);
  const [isUpdatingState, setIsUpdatingState] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [channelOnline, setChannelOnline] = useState(true);
  const [hasWhatsAppChannel, setHasWhatsAppChannel] = useState(true);
  const [conversationTab, setConversationTab] = useState<"all" | "groups" | "channels">("all");
  const [showFilters, setShowFilters] = useState(false);
  const [statusFilter, setStatusFilter] = useState("ALL");
  const messagesEndRef = useRef<HTMLDivElement>(null);
  const messagesScrollRef = useRef<HTMLDivElement>(null);
  const messagesLoadingRef = useRef(false);
  const conversationsScrollRef = useRef<HTMLDivElement>(null);
  const hasMoreConversationsRef = useRef(false);
  const loadingOlderConversationsRef = useRef(false);
  const hasMoreMessagesRef = useRef(false);
  const loadingOlderRef = useRef(false);
  const shouldScrollToBottomRef = useRef(false);
  const isInitialMessageLoadRef = useRef(true);

  const refreshConversations = async () => {
    const response = await fetch("/api/conversations?limit=50", { cache: "no-store" });
    if (!response.ok) return;
    const page = (await response.json()) as ConversationPage;
    hasMoreConversationsRef.current = page.hasMore;
    setConversations((current) => {
      const merged = new Map(current.map((conversation) => [conversation.id, conversation]));
      let changed = false;
      for (const conversation of page.conversations) {
        const previous = merged.get(conversation.id);
        if (!previous || previous.lastMessageAt !== conversation.lastMessageAt ||
          previous.lastActivityAt !== conversation.lastActivityAt ||
          previous.status !== conversation.status ||
          previous.assignedUserId !== conversation.assignedUserId ||
          previous.currentIntent !== conversation.currentIntent) {
          merged.set(conversation.id, conversation);
          changed = true;
        }
      }
      if (!changed) return current;
      return [...merged.values()].sort((left, right) => Date.parse(right.lastMessageAt) - Date.parse(left.lastMessageAt));
    });
  };

  const loadOlderConversations = async () => {
    if (!hasMoreConversationsRef.current || loadingOlderConversationsRef.current || conversations.length === 0) return;
    const oldest = conversations[conversations.length - 1];
    loadingOlderConversationsRef.current = true;
    setIsLoadingOlderConversations(true);
    try {
      const response = await fetch(`/api/conversations?limit=50&before=${encodeURIComponent(oldest.lastMessageAt)}`, { cache: "no-store" });
      if (!response.ok) throw new Error("No se pudieron cargar conversaciones anteriores.");
      const page = (await response.json()) as ConversationPage;
      hasMoreConversationsRef.current = page.hasMore;
      setConversations((current) => [...current, ...page.conversations.filter((conversation) => !current.some((item) => item.id === conversation.id))]);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudieron cargar conversaciones anteriores.");
    } finally {
      loadingOlderConversationsRef.current = false;
      setIsLoadingOlderConversations(false);
    }
  };

  useEffect(() => {
    fetch("/api/conversations?limit=50", { cache: "no-store" })
      .then(async (response) => {
        if (!response.ok) throw new Error("No se pudieron cargar las conversaciones.");
        const page = (await response.json()) as ConversationPage;
        hasMoreConversationsRef.current = page.hasMore;
        setConversations(page.conversations);
        setSelectedId(page.conversations[0]?.id ?? null);
      })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setIsLoading(false));

    const timer = window.setInterval(() => {
      void refreshConversations();
    }, 5000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    const loadChannelStatus = async () => {
      const response = await fetch("/api/channels/whatsapp", { cache: "no-store" });
      if (!response.ok) return;
      const channels = await response.json() as { isOnline?: boolean }[];
      setHasWhatsAppChannel(channels.length > 0);
      setChannelOnline(channels.some((channel) => channel.isOnline));
    };
    void loadChannelStatus();
    const timer = window.setInterval(() => void loadChannelStatus(), 10000);
    return () => window.clearInterval(timer);
  }, []);

  useEffect(() => {
    if (!selectedId) return;
    let cancelled = false;
    messagesLoadingRef.current = false;
    hasMoreMessagesRef.current = false;
    isInitialMessageLoadRef.current = true;
    setIsLoadingMessages(true);
    setMessages([]);
    const loadMessages = async () => {
      if (messagesLoadingRef.current) return;
      messagesLoadingRef.current = true;
      try {
        const [messagesResponse, contextResponse] = await Promise.all([
          fetch(`/api/conversations/${selectedId}/messages?limit=50`, { cache: "no-store" }),
          fetch(`/api/conversations/${selectedId}/context`, { cache: "no-store" }),
        ]);
        if (!messagesResponse.ok) throw new Error("No se pudieron cargar los mensajes.");
        const loadedPage = (await messagesResponse.json()) as MessagePage;
        if (cancelled) return;
        hasMoreMessagesRef.current = loadedPage.hasMore;
        const isInitialLoad = isInitialMessageLoadRef.current;
        const scrollElement = messagesScrollRef.current;
        const isAtBottom = !scrollElement || scrollElement.scrollHeight - scrollElement.scrollTop - scrollElement.clientHeight < 96;
        setMessages((current) => {
          const merged = isInitialLoad
            ? loadedPage.messages
            : [...new Map([...current, ...loadedPage.messages].map((message) => [message.id, message])).values()]
              .sort((left, right) => Date.parse(left.createdAt) - Date.parse(right.createdAt));
          const hasNewLatestMessage = !isInitialLoad && merged.at(-1)?.id !== current.at(-1)?.id;
          shouldScrollToBottomRef.current = isInitialLoad || (isAtBottom && hasNewLatestMessage);
          return merged;
        });
        isInitialMessageLoadRef.current = false;
        if (contextResponse.ok) setContext((await contextResponse.json()) as ConversationContext);
      } catch (reason) {
        if (!cancelled) setError(reason instanceof Error ? reason.message : "No se pudieron cargar los mensajes.");
      } finally {
        messagesLoadingRef.current = false;
        if (!cancelled) setIsLoadingMessages(false);
      }
    };
    void loadMessages();
    const timer = window.setInterval(() => {
      if (!messagesLoadingRef.current) void loadMessages();
    }, 3000);
    return () => { cancelled = true; window.clearInterval(timer); };
  }, [selectedId]);

  useEffect(() => {
    if (shouldScrollToBottomRef.current) {
      messagesEndRef.current?.scrollIntoView({ behavior: "auto", block: "end" });
      shouldScrollToBottomRef.current = false;
    }
  }, [messages]);

  const loadOlderMessages = async () => {
    if (!selectedId || !hasMoreMessagesRef.current || loadingOlderRef.current || messages.length === 0) return;
    const scrollElement = messagesScrollRef.current;
    if (!scrollElement) return;
    loadingOlderRef.current = true;
    setIsLoadingOlderMessages(true);
    const previousHeight = scrollElement.scrollHeight;
    const oldestMessage = messages[0];
    try {
      const response = await fetch(`/api/conversations/${selectedId}/messages?limit=100&before=${encodeURIComponent(oldestMessage.createdAt)}`, { cache: "no-store" });
      if (!response.ok) throw new Error("No se pudieron cargar los mensajes anteriores.");
      const page = (await response.json()) as MessagePage;
      hasMoreMessagesRef.current = page.hasMore;
      setMessages((current) => [...page.messages, ...current]);
      requestAnimationFrame(() => { scrollElement.scrollTop += scrollElement.scrollHeight - previousHeight; });
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "No se pudieron cargar los mensajes anteriores.");
    } finally {
      loadingOlderRef.current = false;
      setIsLoadingOlderMessages(false);
    }
  };

  const selected = conversations.find((conversation) => conversation.id === selectedId) ?? null;
  const filtered = useMemo(
    () => conversations.filter((conversation) => {
      if (isSyntheticDirectConversation(conversation)) return false;
      const matchesTab = conversationTab === "groups" ? conversation.isGroup : conversationTab === "channels" ? conversation.isChannel : !conversation.isGroup && !conversation.isChannel;
      const matchesStatus = statusFilter === "ALL" || conversation.status === statusFilter;
      const matchesSearch = `${conversation.customerName} ${conversation.customerExternalId}`.toLowerCase().includes(search.toLowerCase());
      return matchesTab && matchesStatus && matchesSearch;
    }),
    [conversations, conversationTab, search, statusFilter],
  );

  useEffect(() => {
    if (selectedId && filtered.some((conversation) => conversation.id === selectedId)) return;
    setSelectedId(filtered[0]?.id ?? null);
  }, [conversationTab, filtered, selectedId]);

  const refreshMessages = async () => {
    if (!selectedId) return;
    const response = await fetch(`/api/conversations/${selectedId}/messages?limit=50`, { cache: "no-store" });
    if (response.ok) {
      const page = (await response.json()) as MessagePage;
      setMessages((current) => [...new Map([...current, ...page.messages].map((message) => [message.id, message])).values()]
        .sort((left, right) => Date.parse(left.createdAt) - Date.parse(right.createdAt)));
      hasMoreMessagesRef.current = page.hasMore;
      const scrollElement = messagesScrollRef.current;
      shouldScrollToBottomRef.current = !scrollElement || scrollElement.scrollHeight - scrollElement.scrollTop - scrollElement.clientHeight < 96;
    }
  };

  const sendMessage = async () => {
    if (!selectedId || isLoadingMessages || !draft.trim()) return;
    setIsSending(true);
    const response = await fetch(`/api/conversations/${selectedId}/messages/send`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text: draft, quotedMessageId: replyingTo?.id }),
    });
    if (!response.ok) setError("No se pudo enviar el mensaje.");
    else {
      setDraft("");
      setReplyingTo(null);
      await refreshMessages();
    }
    setIsSending(false);
  };

  const reactToMessage = async (messageId: string, emoji: string) => {
    if (!selectedId) return;
    const response = await fetch(`/api/conversations/${selectedId}/messages/${messageId}/react`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ emoji }) });
    if (!response.ok) setError("No se pudo registrar la reacción.");
  };

  const deleteMessage = async (messageId: string) => {
    if (!selectedId) return;
    const response = await fetch(`/api/conversations/${selectedId}/messages/${messageId}`, { method: "DELETE" });
    if (!response.ok) setError("No se pudo eliminar el mensaje.");
    else await refreshMessages();
  };

  const respondWithAgent = async () => {
    if (!selectedId || isLoadingMessages) return;
    setIsAgentResponding(true);
    const response = await fetch(`/api/conversations/${selectedId}/agent/mock-respond`, { method: "POST" });
    if (!response.ok) setError("No se pudo ejecutar el agente.");
    else await refreshMessages();
    setIsAgentResponding(false);
  };

  const retryMessage = async (messageId: string) => {
    if (!selectedId || isLoadingMessages) return;
    const response = await fetch(`/api/conversations/${selectedId}/messages/${messageId}/retry`, { method: "POST" });
    if (!response.ok) setError("No se pudo reintentar el envío.");
    else await refreshMessages();
  };

  const runConversationAction = async (action: "take-control" | "pause" | "resume" | "close") => {
    if (!selectedId || isLoadingMessages) return;
    setIsUpdatingState(true);
    const response = await fetch(`/api/conversations/${selectedId}/${action}`, { method: "POST" });
    if (!response.ok) setError("No se pudo actualizar el estado de la conversación.");
    else await refreshConversations();
    setIsUpdatingState(false);
  };

  const formatMessageDate = (value: string) => new Date(value).toLocaleDateString("es-PY", { day: "numeric", month: "long", year: "numeric" });
  const formatConversationDate = (value: string) => {
    const date = new Date(value);
    const today = new Date();
    return date.toDateString() === today.toDateString()
      ? date.toLocaleTimeString("es-PY", { hour: "2-digit", minute: "2-digit" })
      : date.toLocaleDateString("es-PY", { day: "2-digit", month: "2-digit", year: "numeric" });
  };

  return (
    <div className="mx-auto flex max-w-[1500px] flex-col gap-5">
      <div className="flex items-end justify-between gap-4 px-1">
        <div>
          <div className="mb-2 flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.18em] text-primary"><MessageCircle className="size-4" /> Canal de ventas</div>
          <h1 className="font-heading text-3xl font-semibold tracking-tight">Conversaciones</h1>
          <p className="mt-2 text-muted-foreground">Tu bandeja de entrada, diseñada para cerrar ventas.</p>
        </div>
        <Link
          href="/dashboard/settings"
          className={cn(
            "flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-medium transition-colors",
            !hasWhatsAppChannel ? "border-destructive/30 bg-destructive/5 text-destructive" : channelOnline ? "border-emerald-500/30 bg-emerald-500/5 text-emerald-700" : "border-amber-500/30 bg-amber-500/5 text-amber-700",
          )}
        >
          <span className={cn("size-2 rounded-full", !hasWhatsAppChannel ? "bg-destructive" : channelOnline ? "bg-emerald-500" : "bg-amber-500")} />
          {!hasWhatsAppChannel ? "Sin WhatsApp conectado" : channelOnline ? "Canal conectado" : "Canal desconectado"}
        </Link>
      </div>

      <div className="flex h-[calc(100vh-12rem)] min-h-[560px] overflow-hidden rounded-[1.5rem] border border-border/80 bg-card shadow-xl shadow-primary/5">
        <aside className={cn("w-full shrink-0 flex-col border-r border-border/70 bg-card sm:flex sm:w-[21rem] lg:w-[24rem]", selectedId ? "hidden" : "flex")}>
          <div className="flex items-center justify-between border-b border-border/70 px-5 py-4"><h2 className="font-heading text-lg font-semibold">Chats</h2><div className="flex items-center gap-1 text-muted-foreground"><button className="rounded-lg p-2 hover:bg-muted" aria-label="Nueva conversación"><Edit3 className="size-4" /></button><button type="button" onClick={() => setShowFilters((visible) => !visible)} className={cn("rounded-lg p-2 hover:bg-muted", showFilters && "bg-muted text-foreground")} aria-label="Filtrar conversaciones"><Filter className="size-4" /></button></div></div>
          <div className="border-b border-border/70 px-4 py-3"><div className="relative"><Search className="absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" /><Input value={search} onChange={(event) => setSearch(event.target.value)} className="h-9 border-0 bg-muted/60 pl-9 shadow-none" placeholder="Buscar o iniciar chat" /></div><div className="mt-3 grid grid-cols-3 gap-1 rounded-lg bg-muted p-1"><button type="button" onClick={() => setConversationTab("all")} className={cn("rounded-md px-2 py-1.5 text-xs font-medium", conversationTab === "all" ? "bg-background text-foreground shadow-sm" : "text-muted-foreground")}>Todos</button><button type="button" onClick={() => setConversationTab("groups")} className={cn("rounded-md px-2 py-1.5 text-xs font-medium", conversationTab === "groups" ? "bg-background text-foreground shadow-sm" : "text-muted-foreground")}>Grupos</button><button type="button" onClick={() => setConversationTab("channels")} className={cn("rounded-md px-2 py-1.5 text-xs font-medium", conversationTab === "channels" ? "bg-background text-foreground shadow-sm" : "text-muted-foreground")}>Canales</button></div>{showFilters && <div className="mt-3 rounded-lg border border-border/70 bg-background p-3"><label className="text-xs font-semibold text-muted-foreground" htmlFor="conversation-status-filter">Estado</label><select id="conversation-status-filter" value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} className="mt-1 h-9 w-full rounded-md border border-input bg-background px-2 text-sm"><option value="ALL">Todos los estados</option><option value="AI_ACTIVE">IA activa</option><option value="HUMAN_ACTIVE">Atendido por vos</option><option value="PAUSED">Pausado</option><option value="CLOSED">Cerrado</option></select></div>}</div>
          <div ref={conversationsScrollRef} onScroll={(event) => { const element = event.currentTarget; if (element.scrollTop + element.clientHeight >= element.scrollHeight - 64) void loadOlderConversations(); }} className="conversation-scroll min-h-0 flex-1 overflow-y-scroll p-2">
            {isLoading && <div className="space-y-2 p-2"><Skeleton className="h-16" /><Skeleton className="h-16" /><Skeleton className="h-16" /></div>}
            {!isLoading && filtered.length === 0 && <EmptyState icon={MessageCircle} title="No hay conversaciones" description={search ? "Probá con otro contacto." : "Las conversaciones nuevas aparecerán aquí."} className="min-h-48 border-0 bg-transparent" />}
            {filtered.map((conversation) => <Link key={conversation.id} href={`/dashboard/conversations/${conversation.id}`} onClick={(event) => { event.preventDefault(); if (!isLoadingMessages) setSelectedId(conversation.id); }} aria-disabled={isLoadingMessages} className={cn("flex items-center gap-3 rounded-xl px-3 py-3 transition-colors hover:bg-muted", isLoadingMessages && "cursor-wait opacity-60", selectedId === conversation.id && "bg-secondary")}><span className="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-full bg-primary text-sm font-semibold text-primary-foreground">{conversation.customerImageUrl ? <img src={conversation.customerImageUrl} alt="" className="size-full object-cover" /> : conversation.customerName.slice(0, 2).toUpperCase()}</span><span className="min-w-0 flex-1"><span className="block truncate text-sm font-semibold">{conversation.customerName}</span><span className="block truncate text-xs text-muted-foreground">{conversation.customerExternalId} · {STATUS_LABELS[conversation.status] ?? conversation.status}</span></span><span className="shrink-0 text-[11px] text-muted-foreground">{formatConversationDate(conversation.lastMessageAt)}</span><span className={cn("size-2 rounded-full", conversation.status === "AI_ACTIVE" && "bg-emerald-500", conversation.status === "HUMAN_ACTIVE" && "bg-blue-500", conversation.status === "PAUSED" && "bg-amber-500", conversation.status === "CLOSED" && "bg-muted-foreground")} aria-label={STATUS_LABELS[conversation.status] ?? conversation.status} /></Link>)}
            {!isLoading && isLoadingOlderConversations && <p className="px-3 py-2 text-center text-xs text-muted-foreground">Cargando conversaciones anteriores...</p>}
          </div>
          <div className="border-t border-border/70 p-4"><div className="flex items-center gap-3"><span className="flex size-9 items-center justify-center rounded-full bg-accent text-sm font-bold text-accent-foreground">V</span><div><p className="text-sm font-semibold">VANTA Agent</p><p className="text-xs text-muted-foreground">Operando en segundo plano</p></div></div></div>
        </aside>

        <section className={cn("min-h-0 min-w-0 flex-1 flex-col bg-background", selectedId ? "flex" : "hidden sm:flex")}>
          {selected ? <>
            <header className="flex shrink-0 flex-col gap-3 border-b border-border/70 bg-card/80 px-6 py-4 backdrop-blur-xl">
              <div className="flex items-center justify-between">
                <div className="flex min-w-0 items-center gap-3"><button type="button" onClick={() => setSelectedId(null)} aria-label="Volver a conversaciones" className="flex size-10 shrink-0 items-center justify-center rounded-xl hover:bg-muted sm:hidden"><ArrowLeft className="size-4" /></button><span className="flex size-10 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-semibold text-primary-foreground">{selected.customerName.slice(0, 2).toUpperCase()}</span><div className="min-w-0"><h2 className="truncate font-heading font-semibold">{selected.customerName}</h2><p className="truncate text-xs text-muted-foreground">{selected.customerExternalId} · <span className={cn(selected.status === "AI_ACTIVE" && "text-emerald-600", selected.status === "HUMAN_ACTIVE" && "text-blue-600", selected.status === "PAUSED" && "text-amber-600", selected.status === "CLOSED" && "text-muted-foreground")}>{STATUS_LABELS[selected.status] ?? selected.status}</span>{selected.currentIntent && <> · Intención: {selected.currentIntent}</>}</p></div></div>
                <div className="flex items-center gap-1 text-muted-foreground"><button className="rounded-lg p-2 hover:bg-muted" aria-label="Videollamada"><Video className="size-4" /></button><button className="rounded-lg p-2 hover:bg-muted" aria-label="Llamar"><Phone className="size-4" /></button><button className="rounded-lg p-2 hover:bg-muted" aria-label="Buscar mensajes"><Search className="size-4" /></button></div>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <button type="button" disabled={isUpdatingState || selected.status === "HUMAN_ACTIVE" || selected.status === "CLOSED"} onClick={() => void runConversationAction("take-control")} className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-medium hover:bg-muted disabled:opacity-40"><ShieldCheck className="size-3.5" /> Tomar control</button>
                <button type="button" disabled={isUpdatingState || selected.status === "PAUSED" || selected.status === "CLOSED"} onClick={() => void runConversationAction("pause")} className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-medium hover:bg-muted disabled:opacity-40"><Pause className="size-3.5" /> Pausar IA</button>
                <button type="button" disabled={isUpdatingState || selected.status === "AI_ACTIVE" || selected.status === "CLOSED"} onClick={() => void runConversationAction("resume")} className="inline-flex items-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-medium hover:bg-muted disabled:opacity-40"><Play className="size-3.5" /> Activar IA</button>
                {context?.cart && context.cart.items.length > 0 && <span className="ml-auto rounded-full bg-muted px-3 py-1 text-xs font-medium">Carrito: {context.cart.total.toLocaleString("es-PY")} Gs.</span>}
                {context?.latestOrder && <span className="rounded-full bg-muted px-3 py-1 text-xs font-medium">Pedido {context.latestOrder.number}: {context.latestOrder.status}</span>}
              </div>
            </header>
            <div ref={messagesScrollRef} onScroll={(event) => { if (event.currentTarget.scrollTop <= 48) void loadOlderMessages(); }} className="conversation-scroll min-h-0 flex-1 overflow-y-scroll px-6 py-6"><div className="mx-auto flex max-w-3xl flex-col gap-4">
              {!channelOnline && <p className="rounded-lg border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-sm font-medium text-amber-800">El número no está en línea.</p>}
              {error && <p className="rounded-lg border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">{error}</p>}
              {isLoadingMessages && <div className="flex items-center justify-center rounded-xl border border-border/70 bg-card px-4 py-3 text-sm text-muted-foreground"><span className="mr-2 size-2 animate-pulse rounded-full bg-primary" />Cargando todos los mensajes...</div>}
              {!isLoadingMessages && isLoadingOlderMessages && <div className="self-center text-xs text-muted-foreground">Cargando conversaciones anteriores...</div>}
              {!isLoadingMessages && messages.length === 0 && <EmptyState icon={MessageCircle} title="Todavía no hay mensajes" description="Cuando el cliente escriba, la conversación aparecerá aquí." className="border-0 bg-transparent" />}
              {messages.map((message, index) => <div key={message.id} className="flex flex-col gap-4">{(index === 0 || formatMessageDate(messages[index - 1].createdAt) !== formatMessageDate(message.createdAt)) && <div className="self-center rounded-full bg-muted px-3 py-1 text-[11px] font-medium text-muted-foreground shadow-sm">{formatMessageDate(message.createdAt)}</div>}<ConversationMessageBubble message={message} onRetry={(messageId) => void retryMessage(messageId)} onReply={setReplyingTo} onReact={(messageId, emoji) => void reactToMessage(messageId, emoji)} onDelete={(messageId) => void deleteMessage(messageId)} /></div>)}
              <div ref={messagesEndRef} />
            </div></div>
            <div className="border-t border-border/70 bg-card px-6 py-4"><div className="mx-auto max-w-3xl">{replyingTo && <div className="mb-2 flex items-center justify-between rounded-lg bg-muted px-3 py-2 text-xs"><span className="flex min-w-0 items-center gap-2 truncate"><Reply className="size-3" />{replyingTo.text || "Archivo adjunto"}</span><button type="button" onClick={() => setReplyingTo(null)} aria-label="Cancelar respuesta"><X className="size-3" /></button></div>}<div className="flex items-center gap-3"><Input disabled={isLoadingMessages} value={draft} onChange={(event) => setDraft(event.target.value)} placeholder={isLoadingMessages ? "Cargando mensajes..." : replyingTo ? "Escribí una respuesta..." : "Escribí un mensaje..."} onKeyDown={(event) => { if (event.key === "Enter") void sendMessage(); }} /><button type="button" disabled={isLoadingMessages || isSending || !draft.trim()} onClick={() => void sendMessage()} className="flex size-10 shrink-0 items-center justify-center rounded-xl bg-primary text-primary-foreground disabled:opacity-50" aria-label="Enviar mensaje"><Send className="size-4" /></button><button type="button" disabled={isLoadingMessages || isAgentResponding} onClick={() => void respondWithAgent()} className="flex size-10 shrink-0 items-center justify-center rounded-xl border hover:bg-muted disabled:opacity-50" aria-label="Responder con agente"><Sparkles className="size-4" /></button></div></div></div>
          </> : <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">Seleccioná una conversación.</div>}
        </section>
      </div>
    </div>
  );
}

