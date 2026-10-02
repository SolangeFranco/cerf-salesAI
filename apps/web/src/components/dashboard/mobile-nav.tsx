"use client";

import { Menu, X } from "lucide-react";
import { useState } from "react";
import { SidebarNav } from "@/components/dashboard/sidebar-nav";

export function MobileNav() {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button type="button" onClick={() => setOpen(true)} aria-label="Abrir navegación" className="flex size-10 items-center justify-center rounded-xl border border-[#e1e3dc] bg-white text-[#34382e] shadow-[0_4px_12px_rgba(30,39,24,0.04)] sm:hidden">
        <Menu className="size-5" />
      </button>
      {open && <div className="fixed inset-0 z-50 sm:hidden" role="dialog" aria-modal="true" aria-label="Navegación principal">
        <button type="button" aria-label="Cerrar navegación" onClick={() => setOpen(false)} className="absolute inset-0 bg-[#11150e]/30" />
        <aside className="relative flex h-full w-[min(82vw,300px)] flex-col gap-8 bg-[#fafaf7] px-4 py-5 shadow-2xl">
          <div className="flex items-center justify-between"><div className="flex items-center gap-2"><span className="flex size-9 items-center justify-center rounded-full bg-[#11150e] text-sm font-black text-[#f4f2e9]">V</span><span className="font-semibold text-[#11150e]">VANTA</span></div><button type="button" onClick={() => setOpen(false)} aria-label="Cerrar navegación" className="flex size-10 items-center justify-center rounded-xl hover:bg-[#eff8f4]"><X className="size-5" /></button></div>
          <div onClick={() => setOpen(false)}><SidebarNav /></div>
        </aside>
      </div>}
    </>
  );
}
