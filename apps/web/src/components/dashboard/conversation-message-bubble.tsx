import Image from "next/image";
import { FileText, Heart, Link2, Reply, ThumbsUp, Trash2, Volume2 } from "lucide-react";
import { cn } from "@/lib/utils";

type ConversationMessage = {
  id: string;
  direction: string;
  text: string;
  audioTranscript?: string | null;
  imageBase64: string | null;
  imageMediaType: string | null;
  mediaBase64?: string | null;
  mediaMimeType?: string | null;
  mediaFileName?: string | null;
  mediaKind?: string | null;
  mediaUnavailable?: boolean;
  quotedExternalMessageId?: string | null;
  quotedText?: string | null;
  isDeleted?: boolean;
  deliveryStatus?: string | null;
  deliveryAttempts?: number;
  deliveryError?: string | null;
  reactionsJson?: string | null;
  createdAt: string;
};

type ConversationMessageBubbleProps = {
  message: ConversationMessage;
  compact?: boolean;
  onRetry?: (messageId: string) => void;
  onReply?: (message: ConversationMessage) => void;
  onReact?: (messageId: string, emoji: string) => void;
  onDelete?: (messageId: string) => void;
};

const directionLabels: Record<string, string> = { Inbound: "Cliente", Outbound: "VANTA Agent" };
const deliveryLabels: Record<string, string> = { Pending: "Pendiente", Sent: "Enviado", Failed: "Fallido" };

function MessageText({ text }: { text: string }) {
  const parts = text.split(/(https?:\/\/[^\s]+)/g);
  return <p className="whitespace-pre-wrap leading-6">{parts.map((part, index) => /^https?:\/\//.test(part)
    ? <a key={`${part}-${index}`} href={part} target="_blank" rel="noreferrer" className="inline-flex max-w-full items-center gap-1 break-all underline underline-offset-2"><Link2 className="size-3 shrink-0" />{part}</a>
    : part)}</p>;
}

export function ConversationMessageBubble({ message, compact = false, onRetry, onReply, onReact, onDelete }: ConversationMessageBubbleProps) {
  const isInbound = message.direction === "Inbound";
  const reactions = (() => {
    if (!message.reactionsJson) return [];
    try {
      return Object.entries(JSON.parse(message.reactionsJson) as Record<string, string[]>).map(([emoji, senders]) => ({ emoji, count: senders.length }));
    } catch {
      return [];
    }
  })();
  const mediaSource = message.mediaBase64 ? `data:${message.mediaMimeType || "application/octet-stream"};base64,${message.mediaBase64}` : null;
  const mediaLabel = message.mediaFileName || (message.mediaKind === "audio" ? "Audio" : message.mediaKind === "video" ? "Video" : "Archivo adjunto");
  return (
    <div className={cn(
      compact ? "rounded-lg border px-3 py-2" : "max-w-[75%] rounded-2xl px-4 py-3 shadow-sm",
      "text-sm",
      isInbound ? (compact ? "bg-muted/40" : "self-start rounded-tl-sm bg-card ring-1 ring-border/70") : (compact ? "bg-background" : "self-end rounded-tr-sm bg-primary text-primary-foreground"),
    )}>
      {message.quotedText && <div className={cn("mb-2 border-l-2 px-2 py-1 text-xs", isInbound ? "border-primary/50 bg-muted/60 text-muted-foreground" : "border-primary-foreground/50 bg-primary-foreground/10 text-primary-foreground/80")}><p className="truncate">{message.quotedText}</p></div>}
      {message.isDeleted ? <p className="italic opacity-70">Mensaje eliminado.</p> : <>
      {message.imageBase64 && <Image src={`data:${message.imageMediaType || "image/jpeg"};base64,${message.imageBase64}`} alt="Imagen del producto" width={640} height={480} unoptimized className="mb-2 max-h-72 max-w-full rounded-lg object-contain" />}
      {mediaSource && message.mediaKind === "image" && <Image src={mediaSource} alt="Imagen compartida" width={640} height={480} unoptimized className="mb-2 max-h-72 max-w-full rounded-lg object-contain" />}
      {mediaSource && message.mediaKind === "sticker" && <Image src={mediaSource} alt="Sticker compartido" width={320} height={320} unoptimized className="mb-2 size-40 object-contain" />}
      {mediaSource && message.mediaKind === "video" && <video controls preload="metadata" className="mb-2 max-h-72 max-w-full rounded-lg"><source src={mediaSource} type={message.mediaMimeType ?? undefined} />Tu navegador no puede reproducir este video.</video>}
      {mediaSource && message.mediaKind === "audio" && <div className="mb-2 flex items-center gap-2"><Volume2 className="size-4 shrink-0" /><audio controls preload="metadata" className="max-w-full"><source src={mediaSource} type={message.mediaMimeType ?? undefined} />Tu navegador no puede reproducir este audio.</audio></div>}
      {message.audioTranscript && <p className={cn("mb-2 border-l-2 px-2 py-1 text-xs", isInbound ? "border-primary/50 bg-muted/60 text-muted-foreground" : "border-primary-foreground/50 bg-primary-foreground/10 text-primary-foreground/80")}>Transcripción: {message.audioTranscript}</p>}
      {mediaSource && message.mediaKind === "document" && <a href={mediaSource} download={message.mediaFileName ?? "archivo"} className={cn("mb-2 flex items-center gap-2 rounded-lg border px-3 py-2 text-xs font-medium", isInbound ? "border-border bg-muted/60" : "border-primary-foreground/30 bg-primary-foreground/10")}><FileText className="size-4 shrink-0" /><span className="truncate">{mediaLabel}</span></a>}
      {message.mediaUnavailable && <p className="mb-2 text-xs italic opacity-75">{mediaLabel} no disponible para descargar.</p>}
      {message.text && <MessageText text={message.text} />}
      </>}
      {reactions.length > 0 && <div className="mt-2 flex flex-wrap gap-1">{reactions.map((reaction) => <span key={reaction.emoji} className={cn("rounded-full px-2 py-0.5 text-xs", isInbound ? "bg-muted text-foreground" : "bg-primary-foreground/15 text-primary-foreground")}>{reaction.emoji} {reaction.count}</span>)}</div>}
      <p className={cn("mt-2 text-[10px]", isInbound ? "text-muted-foreground" : "text-primary-foreground/65")}>{directionLabels[message.direction] ?? message.direction} · {new Date(message.createdAt).toLocaleTimeString("es-PY", { hour: "2-digit", minute: "2-digit" })}{message.deliveryStatus && ` · ${deliveryLabels[message.deliveryStatus] ?? message.deliveryStatus}`}</p>
      {!compact && !message.isDeleted && <div className="mt-2 flex items-center gap-1"><button type="button" onClick={() => onReact?.(message.id, "👍")} className="rounded p-1 hover:bg-black/10" aria-label="Reaccionar con me gusta"><ThumbsUp className="size-3" /></button><button type="button" onClick={() => onReact?.(message.id, "❤️")} className="rounded p-1 hover:bg-black/10" aria-label="Reaccionar con corazón"><Heart className="size-3" /></button><button type="button" onClick={() => onReply?.(message)} className="rounded p-1 hover:bg-black/10" aria-label="Responder"><Reply className="size-3" /></button>{message.direction === "Outbound" && <button type="button" onClick={() => onDelete?.(message.id)} className="rounded p-1 hover:bg-black/10" aria-label="Eliminar mensaje"><Trash2 className="size-3" /></button>}</div>}
      {message.deliveryStatus === "Failed" && onRetry && <button type="button" onClick={() => onRetry(message.id)} className="mt-2 text-xs font-medium underline underline-offset-2">Reintentar envío</button>}
    </div>
  );
}
