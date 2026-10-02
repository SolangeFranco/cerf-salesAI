import { redirect } from "next/navigation";
import Link from "next/link";
import { getCurrentTenant } from "@/lib/api-server";
import { GlobalSearch } from "@/components/dashboard/global-search";
import {
  Bell,
  ChevronDown,
  CircleHelp,
  LayoutDashboard,
  LogOut,
  MessageSquare,
  Package,
  Settings,
  ShoppingBag,
  Users,
} from "lucide-react";

export default async function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const tenant = await getCurrentTenant();
  if (!tenant) {
    redirect("/login");
  }
  if (!tenant.storeSetupCompleted) {
    redirect("/onboarding/whatsapp");
  }

  const navigation = [
    { label: "Inicio", href: "/dashboard", icon: LayoutDashboard, active: true },
    { label: "Pedidos", href: "/dashboard/orders", icon: ShoppingBag },
    { label: "Productos", href: "/dashboard/products", icon: Package },
    { label: "Clientes", href: "/dashboard/customers", icon: Users },
    { label: "Conversaciones", href: "/dashboard/conversations", icon: MessageSquare },
  ];

  return (
    <div className="flex h-screen w-full overflow-hidden bg-[#f4f0ee] text-[#1d2125]">
      <aside className="hidden h-screen w-[240px] shrink-0 flex-col overflow-y-auto border-r border-[#e8e2dd] bg-[#faf8f6] px-4 py-5 md:flex">
        <div className="flex min-h-full w-full flex-col">
          <div className="mb-8 flex items-center gap-2.5 px-2">
            <span className="flex size-8 items-center justify-center rounded-[10px] bg-[#1a1a1a] text-[12px] font-black text-white shadow-sm">V</span>
            <span className="text-[18px] font-semibold tracking-[-0.06em] text-[#17191d]">VANTA</span>
          </div>
          <nav className="space-y-1.5">
            {navigation.map(({ label, href, icon: Icon, active }) => (
              <Link key={label} href={href} className={`flex w-full items-center gap-3 rounded-[14px] px-3 py-2.5 text-left text-[13px] font-medium transition ${active ? "bg-[#e6e1ff] text-[#17191d] shadow-[inset_0_0_0_1px_rgba(122,66,216,0.08)]" : "text-[#667078] hover:bg-[#f0ece9]"}`}>
                <Icon className={`size-4 ${active ? "text-[#6d3de0]" : "text-[#788188]"}`} />
                {label}
              </Link>
            ))}
          </nav>
        <nav className="mt-auto w-full space-y-1.5 border-t border-[#e8e2de] pt-4">
          {[
            { label: "Configuración", href: "/dashboard/settings", icon: Settings },
            { label: "Soporte", href: "#support", icon: CircleHelp },
            { label: "Cerrar sesión", href: "/login", icon: LogOut },
          ].map(({ label, href, icon: Icon }) => (
            <Link key={label} href={href} className="flex w-full items-center gap-3 rounded-[15px] px-3 py-2.5 text-left text-[13px] text-[#667078] hover:bg-[#eeece9]">
              <Icon className="size-4 text-[#788188]" />
              {label}
            </Link>
          ))}
        </nav>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col bg-[#f7f4f2]">
        <header className="grid min-h-[76px] grid-cols-[1fr_auto_1fr] items-center gap-4 border-b border-[#efe7e2] bg-[#f8f5f3] px-4 py-3 sm:px-6 lg:px-8">
          <div className="flex items-center gap-3 justify-self-start">
            <div className="flex size-10 items-center justify-center rounded-full bg-[#f2ebff] text-[13px] font-bold text-[#5b2cc8] shadow-inner">
              {tenant.name.slice(0, 1).toUpperCase()}
            </div>
            <div className="leading-none">
              <div className="text-[11px] uppercase tracking-[0.16em] text-[#8b8f94]">Tienda</div>
              <div className="mt-1 text-[15px] font-semibold tracking-[-0.04em] text-[#1d2125]">{tenant.name}</div>
            </div>
          </div>

          <GlobalSearch />
          <div className="col-start-3 flex items-center justify-self-end gap-2.5">
            <button aria-label="Messages" className="flex size-9 items-center justify-center rounded-full bg-[#f1efed] text-[#4d555b]"><MessageSquare className="size-4" /></button>
            <button aria-label="Notifications" className="relative flex size-9 items-center justify-center rounded-full bg-[#f1efed] text-[#4d555b]"><Bell className="size-4" /><span className="absolute right-1.5 top-1.5 size-2 rounded-full bg-[#d96a62] ring-2 ring-[#f1efed]" /></button>
            <div className="flex items-center gap-2.5 rounded-full border border-[#e5dfdc] bg-[#f1efed] py-1.5 pl-1.5 pr-2.5">
              <span className="flex size-8 items-center justify-center rounded-full bg-gradient-to-br from-[#ded4c9] to-[#9babb7] text-[11px] font-bold text-[#252a30]">{tenant.name.slice(0, 1).toUpperCase()}</span>
              <span className="hidden text-left sm:block"><span className="block max-w-28 truncate text-[11px] font-semibold text-[#282d32]">{tenant.name}</span><span className="block text-[10px] text-[#7a8187]">Admin</span></span>
              <ChevronDown className="size-3.5 text-[#687078]" />
            </div>
          </div>
        </header>
        <main id="main-content" className="flex-1 overflow-y-auto bg-[#f5f1ee] px-4 py-5 sm:px-6 lg:px-8 lg:py-7">{children}</main>
      </div>
    </div>
  );
}
