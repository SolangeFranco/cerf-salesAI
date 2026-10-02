"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";
import { Badge } from "@/components/ui/badge";
import { BarChart3, Bell, Box, CalendarDays, ChartNoAxesCombined, CircleHelp, FileText, MessageCircle, Settings2, ShoppingBag, Users, Zap } from "lucide-react";

const navItems = [
  { section: "Main", href: "/dashboard", label: "Dashboard", enabled: true, icon: BarChart3 },
  { section: "Main", href: "/dashboard/conversations", label: "Inbox", enabled: true, icon: MessageCircle },
  { section: "Main", href: "/dashboard/calendar", label: "Calendar", enabled: false, icon: CalendarDays },
  { section: "Main", href: "/dashboard/orders", label: "Orders", enabled: true, icon: ShoppingBag },
  { section: "Main", href: "/dashboard/invoices", label: "Invoices", enabled: false, icon: FileText },
  { section: "Main", href: "/dashboard/products", label: "Products", enabled: true, icon: Box },
  { section: "Main", href: "/dashboard/customers", label: "Clients", enabled: true, icon: Users },
  { section: "Other", href: "/dashboard/activity", label: "Activity Log", enabled: false, icon: Zap },
  { section: "Other", href: "/dashboard/settings", label: "Settings", enabled: true, icon: Settings2 },
  { section: "Other", href: "/help", label: "Help & Support", enabled: false, icon: CircleHelp },
];

export function SidebarNav() {
  const pathname = usePathname();

  return (
    <nav className="flex min-w-0 flex-col gap-1">
      {navItems.map((item, index) => {
        const isActive = pathname === item.href;

        const showSection = index === 0 || navItems[index - 1].section !== item.section;

        if (!item.enabled) {
          return (
            <div key={item.href}>
              {showSection && <p className="mb-2 mt-3 px-3 text-[10px] font-semibold uppercase tracking-[0.16em] text-[#9aa7b1]">{item.section}</p>}
              <span className="flex items-center justify-between rounded-xl px-3 py-2.5 text-sm text-[#a3afb8]"><span className="flex items-center gap-3"><item.icon className="size-4 shrink-0" /><span>{item.label}</span></span><Badge variant="secondary" className="border-[#dce5ed] bg-[#f4f7fa] text-[10px] text-[#7d8993]">Pronto</Badge></span>
            </div>
          );
        }

        return (
          <div key={item.href}>
            {showSection && <p className="mb-2 mt-3 px-3 text-[10px] font-semibold uppercase tracking-[0.16em] text-[#9aa7b1]">{item.section}</p>}
            <Link href={item.href} className={cn("flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors hover:bg-[#edf6fd] hover:text-[#3976a5]", isActive ? "bg-[#edf6fd] text-[#3976a5] shadow-[inset_3px_0_0_#8fc2f0]" : "text-[#667581]")}><item.icon className="size-4 shrink-0" /><span>{item.label}</span></Link>
          </div>
        );
      })}
    </nav>
  );
}
