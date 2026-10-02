import Link from "next/link";
import { Suspense } from "react";
import { ArrowLeft, CheckCircle2, MessageCircle, ShieldCheck } from "lucide-react";
import { RegisterForm } from "@/components/auth/register-form";

export default function RegisterPage() {
  return (
    <div className="min-h-screen bg-white text-[#292d32] lg:grid lg:grid-cols-[0.95fr_1.05fr]">
      <aside className="relative hidden overflow-hidden bg-[#292d32] p-10 text-white lg:flex lg:flex-col lg:justify-between xl:p-14">
        <div className="absolute -right-24 top-20 size-[380px] rounded-full bg-[#8fc2f0]/20 blur-3xl" />
        <div className="absolute -left-20 bottom-10 size-[300px] rounded-full bg-[#d2a05a]/15 blur-2xl" />

        <div className="relative z-10 flex items-center gap-2.5">
          <span className="flex size-9 items-center justify-center rounded-full bg-[#77ce69] text-xs font-black text-[#292d32] shadow-sm">
            V
          </span>
          <span className="text-xl font-black tracking-[-0.04em]">
            VANTA<span className="text-[#77ce69]">.</span>
          </span>
        </div>

        <div className="relative z-10 max-w-md">
          <p className="mb-4 text-xs font-bold uppercase tracking-[0.2em] text-[#159447]">
            Piloto en validación
          </p>
          <h2 className="text-5xl font-black leading-[0.92] tracking-[-0.07em] xl:text-6xl">
            Organizá las ventas que llegan por WhatsApp.
          </h2>
          
          <ul className="mt-8 flex flex-col gap-3 text-sm text-[#b3c9c2]">
            <li className="flex items-center gap-2.5">
              <CheckCircle2 className="size-4 shrink-0 text-[#159447]" />
              <span>Herramientas de IA basadas en el catálogo de la tienda</span>
            </li>
            <li className="flex items-center gap-2.5">
              <CheckCircle2 className="size-4 shrink-0 text-[#159447]" />
              <span>Catálogo, variantes y reservas de inventario</span>
            </li>
            <li className="flex items-center gap-2.5">
              <CheckCircle2 className="size-4 shrink-0 text-[#159447]" />
              <span>Transferencia manual con verificación del comercio</span>
            </li>
          </ul>

          <div className="mt-10 inline-flex items-center gap-3 rounded-full border border-white/15 bg-white/10 px-4 py-2.5 text-xs font-bold backdrop-blur-md">
            <span className="flex size-7 items-center justify-center rounded-full bg-[#25d366] text-white">
              <MessageCircle className="size-4" />
            </span>
            Compatible con WhatsApp Cloud API
          </div>
        </div>

        <div className="relative z-10 flex items-center justify-between text-xs font-medium text-[#81a19a]">
          <span>Paraguay primero · LATAM después</span>
          <span>© {new Date().getFullYear()} VANTA</span>
        </div>
      </aside>

      <main className="flex min-h-screen flex-col px-5 py-6 sm:px-10 lg:min-h-0 lg:px-16 lg:py-10 xl:px-24">
        <header className="flex items-center justify-between">
          <Link
            href="/"
            className="inline-flex items-center gap-2 text-xs font-bold text-[#65736d] transition-colors hover:text-[#173c37]"
          >
            <ArrowLeft className="size-4" /> Volver al inicio
          </Link>
          <Link href="/login" className="text-xs font-bold text-[#159447] hover:underline">
            Iniciá sesión
          </Link>
        </header>

        <div className="mx-auto flex w-full max-w-md flex-1 flex-col justify-center py-10">
          <div className="mb-8 flex flex-col gap-3">
            <div className="flex size-10 items-center justify-center rounded-full bg-[#159447] text-xs font-black text-white lg:hidden">
              V
            </div>
            <p className="text-xs font-black uppercase tracking-[0.18em] text-[#159447]">
              Registro de tienda
            </p>
            <h1 className="text-4xl font-black leading-none tracking-[-0.06em] text-[#202526] sm:text-5xl">
              Creá tu cuenta.
            </h1>
            <p className="max-w-sm text-sm leading-6 text-[#65736d]">
              Creá una cuenta para configurar una tienda de prueba y recorrer el flujo disponible.
            </p>
          </div>

          <div className="rounded-[24px] border border-[#dbe6dc] bg-white p-6 shadow-[0_16px_40px_rgba(23,60,55,0.06)] sm:p-8">
            <Suspense>
              <RegisterForm />
            </Suspense>
            <div className="mt-6 flex items-start gap-2 border-t border-[#eaf0eb] pt-5 text-xs leading-5 text-[#889893]">
              <ShieldCheck className="mt-0.5 size-4 shrink-0 text-[#159447]" />
              <span>El registro crea un espacio de trabajo; los planes y precios están en validación.</span>
            </div>
          </div>

          <p className="mt-6 text-center text-xs font-medium text-[#65736d]">
            ¿Ya tenés una cuenta registrada?{" "}
            <Link href="/login" className="font-bold text-[#173c37] underline-offset-4 hover:underline">
              Iniciá sesión aquí
            </Link>
          </p>
        </div>

        <footer className="text-center text-[11px] font-medium text-[#a1aaa4]">
          © {new Date().getFullYear()} VANTA
        </footer>
      </main>
    </div>
  );
}
