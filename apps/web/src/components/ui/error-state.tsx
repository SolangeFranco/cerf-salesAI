import { AlertCircle, RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div role="alert" className="flex min-h-32 flex-col items-center justify-center rounded-xl border border-destructive/25 bg-destructive/5 px-5 py-6 text-center">
      <AlertCircle className="size-5 text-destructive" />
      <p className="mt-2 text-sm font-semibold text-destructive">No pudimos cargar esta vista</p>
      <p className="mt-1 max-w-sm text-xs leading-5 text-destructive/80">{message}</p>
      {onRetry && <Button type="button" variant="outline" size="sm" onClick={onRetry} className="mt-4 gap-2 border-destructive/30 text-destructive hover:bg-destructive/10"><RefreshCw className="size-3.5" /> Reintentar</Button>}
    </div>
  );
}
