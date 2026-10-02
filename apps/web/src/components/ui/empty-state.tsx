import type { LucideIcon } from "lucide-react";
import { cn } from "@/lib/utils";

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  className,
}: {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
}) {
  return (
    <div className={cn("flex min-h-40 flex-col items-center justify-center rounded-xl border border-dashed border-border/80 bg-muted/20 px-5 py-8 text-center", className)}>
      {Icon && <span className="mb-3 flex size-10 items-center justify-center rounded-full bg-primary/10 text-primary"><Icon className="size-5" /></span>}
      <p className="text-sm font-semibold text-foreground">{title}</p>
      {description && <p className="mt-1 max-w-sm text-xs leading-5 text-muted-foreground">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}
