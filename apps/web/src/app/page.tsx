"use client";

import { useState, useRef } from "react";
import Link from "next/link";
import { AnimatePresence, motion, useScroll, useTransform } from "motion/react";
import {
  ArrowRight,
  ArrowUpRight,
  Bot,
  CheckCircle2,
  DollarSign,
  Layers,
  Users,
} from "lucide-react";

// ==========================================
// DATA DEFINITIONS & PRESETS
// ==========================================

const FAQS = [
  {
    q: "¿Necesito conocimientos técnicos o programar para usar VANTA?",
    a: "No se requieren conocimientos de programación para explorar el dashboard. El catálogo se gestiona desde la aplicación y la conexión de proveedores reales requiere configuración y validación del comercio.",
  },
  {
    q: "¿El agente puede equivocarse con los precios o inventar descuentos?",
    a: "La selección actual de herramientas es determinista y las consultas pasan por servicios tenant-scoped. La calidad de providers externos debe validarse con el catálogo real de cada comercio.",
  },
  {
    q: "¿Cómo se gestionan los pagos en Paraguay y la región?",
    a: "El flujo disponible es transferencia bancaria manual: el checkout muestra los datos del comercio y éste verifica el abono antes de confirmar el pago. No hay pasarela automática ni confirmación por comprobante.",
  },
  {
    q: "¿Puedo intervenir si quiero responderle yo mismo a un cliente?",
    a: "Sí, en cualquier momento. Desde tu panel de control puedes pausar la IA en una conversación específica con un clic o tomar el control directamente desde tu teléfono.",
  },
  {
    q: "¿Qué pasa si mi catálogo cambia constantemente?",
    a: "Puedes actualizar productos, precios y stock desde el panel. La sincronización o importación desde catálogos externos no está disponible todavía.",
  },
];

function ReferenceHero() {
  const heroRef = useRef<HTMLElement>(null);
  const { scrollYProgress } = useScroll({ target: heroRef, offset: ["start start", "end start"] });
  const headlineY = useTransform(scrollYProgress, [0, 1], [0, -54]);
  const dashboardY = useTransform(scrollYProgress, [0, 1], [0, 72]);
  const dashboardScale = useTransform(scrollYProgress, [0, 0.75, 1], [1, 0.98, 0.94]);

  return (
    <section ref={heroRef} className="relative z-10 overflow-hidden bg-[#f6f5f1] px-4 pb-4 pt-5 text-[#11150e] sm:px-6 lg:px-8">
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute inset-0 bg-[linear-gradient(135deg,#f8faf8_0%,#eef3ef_38%,#e4ece7_68%,#f5f7f4_100%)]" />
        <div className="absolute -left-32 top-16 h-[42%] w-[56%] rounded-[48%] bg-[#d8edc7]/35 blur-[90px]" />
        <div className="absolute -right-32 bottom-8 h-[48%] w-[58%] rounded-[48%] bg-[#cfe1ef]/35 blur-[100px]" />
        <div className="absolute left-[8%] top-[24%] h-px w-[84%] rotate-[5deg] bg-white/80 blur-[2px]" />
        <div className="absolute left-[10%] top-[25%] h-24 w-[80%] rounded-[50%] bg-white/20 blur-[45px]" />
        <div className="absolute inset-0 bg-[linear-gradient(115deg,transparent_20%,rgba(255,255,255,0.5)_47%,transparent_66%)] opacity-60" />
      </div>
      <motion.div
        initial={{ left: "50%", top: "46%", x: "-50%", y: "-50%", scale: 3.7 }}
        animate={{ left: "max(24px, calc((100vw - 1152px) / 2))", top: "29px", x: 0, y: 0, scale: 1, opacity: 1 }}
        transition={{ duration: 1.2, delay: 0.2, ease: [0.22, 1, 0.36, 1] }}
        className="pointer-events-none absolute z-40 flex items-center gap-2 text-sm font-black tracking-[-0.04em] text-[#11150e]"
      >
        <span className="flex h-10 w-10 items-center justify-center rounded-full bg-[#11150e] text-lg text-[#f4f2e9] shadow-[0_12px_30px_rgba(16,22,11,0.28)] sm:h-12 sm:w-12">V</span>
        <span>VANTA</span>
      </motion.div>
      <div className="mx-auto max-w-7xl">
        <motion.header initial={{ opacity: 0, y: -10 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.55, delay: 1.5 }} className="relative z-30 flex items-center justify-between gap-4 py-3 sm:py-5">
          <Link href="/" className="flex items-center gap-2.5 text-sm font-black tracking-[-0.04em] opacity-0">
            <span className="flex h-7 w-7 items-center justify-center rounded-full bg-[#11150e] text-xs text-[#f4f2e9]">V</span>
            VANTA
          </Link>
          <nav className="hidden items-center gap-1 rounded-full border border-white/70 bg-white/75 p-1.5 text-[11px] font-medium text-[#34382e] shadow-[0_10px_24px_rgba(28,37,24,0.08)] backdrop-blur-xl md:flex">
            <a href="#validacion" className="rounded-full px-4 py-2 transition-colors hover:bg-white hover:text-[#11150e]">Validación</a>
            <a href="#precios" className="rounded-full px-4 py-2 transition-colors hover:bg-white hover:text-[#11150e]">Precios</a>
            <a href="#como-funciona" className="rounded-full px-4 py-2 transition-colors hover:bg-white hover:text-[#11150e]">Cómo funciona</a>
            <a href="#faq" className="rounded-full px-4 py-2 transition-colors hover:bg-white hover:text-[#11150e]">Preguntas</a>
          </nav>
          <div className="flex items-center gap-1 rounded-full border border-white/70 bg-white/75 p-1.5 text-[11px] font-semibold text-[#34382e] shadow-[0_10px_24px_rgba(28,37,24,0.08)] backdrop-blur-xl">
            <Link href="/login" className="rounded-full px-4 py-2 transition-colors hover:bg-white hover:text-[#11150e]">Iniciar sesión</Link>
            <Link href="/register" className="rounded-full bg-[#6d9908] px-4 py-2 text-white shadow-[0_6px_14px_rgba(82,120,8,0.24)] transition-transform hover:-translate-y-0.5 hover:bg-[#5d8505]">Crear cuenta</Link>
          </div>
        </motion.header>

        <motion.div
          initial={{ opacity: 0, y: 18 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.7, ease: "easeOut" }}
          className="relative mt-5 overflow-hidden sm:mt-8"
        >
          <div className="relative overflow-hidden bg-white/[0.08] backdrop-blur-[3px]">

            <motion.div
              initial={{ opacity: 0, y: 16 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.65, delay: 1.78, ease: "easeOut" }}
              style={{ y: headlineY }}
              className="relative z-10 mx-auto mt-14 max-w-2xl px-4 text-center sm:mt-20"
            >
              <h1 className="text-[2.6rem] font-semibold leading-[0.94] tracking-[-0.07em] text-[#11150e] sm:text-[4.5rem]">
                Tu agente de ventas
                <span className="block italic text-[#75a832]">para WhatsApp</span>
              </h1>
              <p className="mx-auto mt-4 max-w-lg text-xs leading-relaxed text-[#5e6658] sm:text-sm">
                Consulta el catálogo de tu comercio, organiza pedidos y conserva el control de cada conversación y pago.
              </p>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 36, scale: 0.97 }}
              animate={{ opacity: 1, y: 0, scale: 1 }}
              transition={{ duration: 0.9, delay: 2.02, ease: "easeOut" }}
              style={{
                y: dashboardY,
                scale: dashboardScale,
                WebkitMaskImage: "linear-gradient(to bottom, black 0%, black 68%, transparent 96%)",
                maskImage: "linear-gradient(to bottom, black 0%, black 68%, transparent 96%)",
              }}
              className="relative z-10 mx-auto mt-10 w-[94%] max-w-[1120px] rounded-[26px] border border-white/80 bg-[#f8f8f5]/90 p-3 pb-12 shadow-[0_26px_60px_rgba(25,43,17,0.2)] backdrop-blur-xl sm:mt-12 sm:p-5 sm:pb-16"
            >
              <div className="rounded-[18px] border border-[#e3e4dd] bg-[#fafaf7] p-4 sm:p-6">
                <div className="mb-5 flex items-center justify-between gap-3">
                  <div><div className="text-base font-bold text-[#172016]">Resumen de ventas</div><div className="mt-1 text-[11px] text-[#7b8177]">Vista ilustrativa · no representa datos reales</div></div>
                  <div className="hidden items-center rounded-full border border-[#dfe3d6] bg-white px-3 py-2 text-[11px] text-[#687060] sm:flex">Ejemplo de interfaz</div>
                </div>

                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  {[
                    ["Conversaciones", "#dff3d2"],
                    ["Pedidos creados", "#e9f1d8"],
                    ["Ventas atribuidas", "#f1ead1"],
                    ["Tiempo de respuesta", "#e1eee0"],
                  ].map(([label, color]) => (
                    <div key={label} className="rounded-xl border border-[#e7e8e1] bg-white p-4">
                      <div className="text-[10px] text-[#7b8177]">{label}</div>
                      <div className="mt-2 text-xl font-black tracking-[-0.04em] text-[#172016] sm:text-2xl">Sin datos</div>
                      <div className="mt-2 inline-flex rounded-full px-2 py-1 text-[10px] font-medium text-[#426239]" style={{ backgroundColor: color }}>Esperando actividad</div>
                    </div>
                  ))}
                </div>

                <div className="mt-4 grid gap-4 lg:grid-cols-[1.6fr_0.9fr]">
                  <div className="rounded-xl border border-[#e7e8e1] bg-white p-4 sm:p-5">
                    <div className="text-sm font-bold">Ingresos por ventas</div>
                    <div className="mt-6 flex h-56 items-center justify-center rounded-lg border border-dashed border-[#e1e3dc] px-6 text-center text-xs text-[#7b8177]">Los ingresos aparecerán cuando haya pedidos pagados.</div>
                  </div>

                  <div className="rounded-xl border border-[#e7e8e1] bg-white p-4 sm:p-5">
                    <div className="text-sm font-bold">Origen de conversaciones</div>
                    <div className="mt-6 flex min-h-48 items-center justify-center rounded-lg border border-dashed border-[#e1e3dc] px-6 text-center text-xs text-[#7b8177]">Sin datos de origen de conversaciones.</div>
                  </div>
                </div>

                <div className="mt-4 grid gap-4 sm:grid-cols-3">
                  {["Conversaciones", "Pedidos", "Recuperaciones"].map((title) => <div key={title} className="flex items-center gap-3 rounded-xl border border-[#e7e8e1] bg-white p-4"><span className="flex h-9 w-9 items-center justify-center rounded-lg bg-[#dff3d2] text-[#41632d]"><CheckCircle2 className="h-4 w-4" /></span><div><div className="text-[11px] font-bold">{title}</div><div className="mt-1 text-[10px] text-[#858b81]">Sin datos</div></div></div>)}
                </div>
              </div>
            </motion.div>
            {/* matches the card's own width/radius so the fade never bleeds past its edges */}
            <div className="pointer-events-none absolute inset-x-0 bottom-0 z-20 mx-auto h-20 w-[94%] max-w-[1120px] rounded-b-[26px] bg-gradient-to-b from-transparent via-[#f6f5f1]/70 to-[#f6f5f1] sm:h-28" />
          </div>
        </motion.div>
      </div>
    </section>
  );
}

type SilkFold = { top: string; left: string; width: string; height: string; rotate: number; color: string };

function SilkFolds({ folds }: { folds: SilkFold[] }) {
  return (
    <>
      {folds.map((fold, i) => (
        <div
          key={i}
          className="absolute rounded-[50%] blur-lg"
          style={{ top: fold.top, left: fold.left, width: fold.width, height: fold.height, transform: `rotate(${fold.rotate}deg)`, background: fold.color }}
        />
      ))}
    </>
  );
}

const SILK_FOLDS: Record<"light" | "dark" | "blue" | "gold", SilkFold[]> = {
  light: [
    { top: "-8%", left: "-22%", width: "145%", height: "34%", rotate: -16, color: "rgba(255,255,255,0.95)" },
    { top: "24%", left: "-14%", width: "135%", height: "28%", rotate: -11, color: "rgba(150,169,182,0.6)" },
    { top: "48%", left: "-20%", width: "145%", height: "32%", rotate: -18, color: "rgba(255,255,255,0.85)" },
    { top: "74%", left: "-14%", width: "135%", height: "26%", rotate: -9, color: "rgba(120,141,157,0.45)" },
  ],
  dark: [
    { top: "-6%", left: "-20%", width: "145%", height: "32%", rotate: -14, color: "rgba(80,84,74,0.65)" },
    { top: "26%", left: "-16%", width: "138%", height: "28%", rotate: -9, color: "rgba(15,16,11,0.6)" },
    { top: "56%", left: "-20%", width: "145%", height: "30%", rotate: -15, color: "rgba(65,69,60,0.55)" },
  ],
  blue: [
    { top: "-8%", left: "-22%", width: "145%", height: "34%", rotate: -16, color: "rgba(230,246,255,0.95)" },
    { top: "24%", left: "-14%", width: "135%", height: "28%", rotate: -11, color: "rgba(45,95,135,0.55)" },
    { top: "48%", left: "-20%", width: "145%", height: "32%", rotate: -18, color: "rgba(210,235,250,0.75)" },
    { top: "74%", left: "-14%", width: "135%", height: "26%", rotate: -9, color: "rgba(15,55,85,0.45)" },
  ],
  gold: [
    { top: "-8%", left: "-22%", width: "145%", height: "34%", rotate: 14, color: "rgba(255,248,222,0.95)" },
    { top: "24%", left: "-14%", width: "135%", height: "28%", rotate: 9, color: "rgba(140,100,35,0.55)" },
    { top: "48%", left: "-20%", width: "145%", height: "32%", rotate: 16, color: "rgba(255,241,202,0.75)" },
    { top: "74%", left: "-14%", width: "135%", height: "26%", rotate: 8, color: "rgba(85,58,18,0.45)" },
  ],
};

function CapabilityVisual({ variant }: { variant: "fabric" | "beam" | "globe" | "coin" }) {
  if (variant === "fabric") {
    return (
      <div className="absolute inset-0 overflow-hidden" style={{ background: "linear-gradient(135deg,#eef2f4 0%,#c3ced6 100%)" }}>
        <SilkFolds folds={SILK_FOLDS.light} />
      </div>
    );
  }
  if (variant === "beam") {
    return (
      <div className="absolute inset-0 overflow-hidden" style={{ background: "linear-gradient(160deg,#2b2e29 0%,#0b0c09 100%)" }}>
        <SilkFolds folds={SILK_FOLDS.dark} />
        <div className="absolute left-1/2 top-0 h-full w-20 -translate-x-1/2 blur-2xl" style={{ background: "linear-gradient(180deg, rgba(255,255,255,0.95), rgba(255,255,255,0.15) 60%, transparent 100%)" }} />
      </div>
    );
  }
  if (variant === "globe") {
    return (
      <div className="absolute inset-0 overflow-hidden" style={{ background: "linear-gradient(135deg,#dcedf7 0%,#3f7ba8 100%)" }}>
        <SilkFolds folds={SILK_FOLDS.blue} />
      </div>
    );
  }
  return (
    <div className="absolute inset-0 overflow-hidden" style={{ background: "linear-gradient(135deg,#f6ecd0 0%,#a97a2f 100%)" }}>
      <SilkFolds folds={SILK_FOLDS.gold} />
    </div>
  );
}

function ReferenceProductSections() {
  const [openQuestion, setOpenQuestion] = useState(0);
  const [isVantaRadarOpen, setIsVantaRadarOpen] = useState(false);
  const capabilitiesRef = useRef<HTMLDivElement>(null);

  return (
    <>
      <section className="bg-[#f6f5f1] px-4 py-20 text-[#11150e] sm:px-6 lg:px-8">
        <div className="mx-auto max-w-3xl text-center">
          <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-[#78a43a]">Etapa de validación</p>
          <h2 className="mt-4 text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Construyamos el piloto<br /><span className="italic text-[#75a832]">con comercios reales</span></h2>
          <p className="mt-5 text-sm leading-relaxed text-[#697066]">Estamos validando el flujo de ventas, el control humano y la transferencia manual con design partners. Aún no publicamos testimonios ni resultados de clientes.</p>
        </div>
      </section>

      <section className="bg-[#f6f5f1] px-4 py-24 text-[#11150e] sm:px-6 lg:px-8">
        <div className="mx-auto max-w-6xl overflow-hidden rounded-[36px] bg-[#eceae3] pb-8 pt-10 sm:pt-14 sm:pb-10">
          <div className="flex flex-col items-start justify-between gap-6 px-6 sm:flex-row sm:items-end sm:px-10">
            <div className="max-w-xl">
              <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">
                Herramientas para
                <span className="block italic text-[#75a832]">ordenar ventas por chat</span>
              </h2>
              <p className="mt-5 text-sm leading-relaxed text-[#697066]">Catálogo, conversaciones, pedidos y estados de pago en un mismo espacio de trabajo.</p>
            </div>
            <div className="flex gap-2">
              <button aria-label="Anterior" onClick={() => capabilitiesRef.current?.scrollBy({ left: -416, behavior: "smooth" })} className="flex h-10 w-10 items-center justify-center rounded-full border border-[#dfe2d9] bg-white text-[#a3a89b] shadow-[0_6px_16px_rgba(30,39,24,0.05)]"><ArrowRight className="h-4 w-4 rotate-180" /></button>
              <button aria-label="Siguiente" onClick={() => capabilitiesRef.current?.scrollBy({ left: 416, behavior: "smooth" })} className="flex h-10 w-10 items-center justify-center rounded-full bg-[#6d9908] text-white shadow-[0_10px_20px_rgba(82,120,8,0.24)]"><ArrowRight className="h-4 w-4" /></button>
            </div>
          </div>

          <div
            ref={capabilitiesRef}
            className="mt-12 flex gap-4 overflow-x-auto scroll-smooth px-6 pb-2 sm:px-10 [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden"
          >
            {[
              { title: "Respuestas con IA", description: "El agente usa herramientas controladas para consultar el catálogo del comercio y preparar respuestas basadas en esos datos.", icon: Bot, variant: "fabric" as const },
              { title: "Control humano", description: "El merchant puede pausar la IA, tomar control de una conversación y reanudarla desde el dashboard.", icon: Users, variant: "beam" as const },
              { title: "Pedidos trazables", description: "Las órdenes conservan productos, importes, estados e historial dentro del tenant.", icon: Layers, variant: "globe" as const },
              { title: "Transferencia manual", description: "El checkout muestra los datos bancarios configurados. El comercio verifica el abono y confirma el pago manualmente.", icon: DollarSign, variant: "coin" as const },
            ].map((card, index) => {
              const Icon = card.icon;
              return (
                <motion.article
                  key={card.title}
                  initial={{ opacity: 0, x: index === 0 ? -180 : index === 3 ? 180 : 0, y: index === 1 || index === 2 ? 30 : 0 }}
                  whileInView={{ opacity: 1, x: 0, y: 0 }}
                  viewport={{ once: true, amount: 0.2 }}
                  transition={{ duration: 0.65, delay: index * 0.08, ease: "easeOut" }}
                  className="w-[280px] shrink-0 overflow-hidden rounded-[20px] border border-[#e1e3dc] bg-white shadow-[0_10px_24px_rgba(30,39,24,0.06)] sm:w-[400px]"
                >
                  <div className="relative h-44 overflow-hidden">
                    <CapabilityVisual variant={card.variant} />
                    <span className="absolute left-4 top-4 z-10 flex h-9 w-9 items-center justify-center rounded-lg bg-white/95 text-[#11150e] shadow-[0_6px_14px_rgba(0,0,0,0.15)]"><Icon className="h-4 w-4" /></span>
                  </div>
                  <div className="p-6">
                    <h3 className="text-base font-bold tracking-[-0.02em]">{card.title}</h3>
                    <p className="mt-3 text-[11px] leading-relaxed text-[#697066]">{card.description}</p>
                  </div>
                </motion.article>
              );
            })}
          </div>
        </div>
      </section>

      <section id="como-funciona" className="bg-[#f6f5f1] px-4 py-24 text-[#11150e] sm:px-6 lg:px-8">
        <div className="mx-auto max-w-6xl">
          <div className="flex flex-col items-start justify-between gap-6 sm:flex-row sm:items-end sm:px-10">
            <div className="max-w-xl">
              <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">
                Un flujo claro
                <span className="block italic text-[#75a832]">para cada conversación</span>
              </h2>
            </div>
            <div className="max-w-xs">
              <p className="text-sm leading-relaxed text-[#697066]">Consulta el catálogo, registra el pedido y permite que el comercio supervise el pago.</p>
              <Link href="/register" className="mt-4 inline-flex items-center gap-2 rounded-full bg-[#6d9908] px-5 py-2.5 text-xs font-bold text-white shadow-[0_10px_20px_rgba(82,120,8,0.24)] transition-transform hover:-translate-y-0.5">Crear cuenta</Link>
            </div>
          </div>

          <div className="mt-16 grid gap-4 md:grid-cols-4">
            {[
              { number: "01", title: "Recibe la consulta", description: "El mensaje entra por el canal conectado y se registra en la conversación.", tone: "green" as const, photo: "/images/chat-whatsapp.jpeg" },
              { number: "02", title: "Consulta el catálogo", description: "Las herramientas consultan productos y disponibilidad configurados antes de responder.", tone: "dark" as const, photo: "/images/ask-ia.jpeg" },
              { number: "03", title: "Crea el pedido", description: "El pedido se registra con sus artículos, importe y estado de pago.", tone: "green" as const, photo: "/images/confirm-order.jpeg" },
              { number: "04", title: "Verifica la transferencia", description: "El comercio comprueba el depósito y confirma manualmente el pago en VANTA.", tone: "dark" as const, photo: "/images/handshake-deal.jpg" },
            ].map((step, index) => {
              const fromSide = index === 0 ? -160 : index === 3 ? 160 : 0;
              const toneGradient = step.tone === "green" ? "linear-gradient(165deg, rgba(190,230,160,0.55) 0%, rgba(90,140,75,0.5) 55%, rgba(20,32,14,0.6) 100%)" : "linear-gradient(165deg, rgba(150,170,178,0.5) 0%, rgba(70,90,96,0.5) 55%, rgba(12,16,18,0.6) 100%)";
              return (
                <motion.article
                  key={step.number}
                  initial={{ opacity: 0, x: fromSide, y: fromSide === 0 ? 30 : 0 }}
                  whileInView={{ opacity: 1, x: 0, y: 0 }}
                  viewport={{ once: true, amount: 0.2 }}
                  transition={{ duration: 0.6, delay: index * 0.08, ease: "easeOut" }}
                  className={`relative h-72 overflow-hidden rounded-[20px] text-white shadow-[0_20px_45px_rgba(20,26,16,0.22)] ${index % 2 === 1 ? "md:-mt-8" : "md:mt-8"}`}
                >
                  {step.photo ? (
                    <img src={step.photo} alt="" className="absolute inset-0 h-full w-full object-cover" />
                  ) : (
                    <div className="absolute inset-0" style={{ background: toneGradient }} />
                  )}
                  {step.photo && <div className="pointer-events-none absolute inset-0 bg-gradient-to-b from-black/30 via-black/15 to-black/45" />}

                  {/* inset glass panel: leaves a thin margin of the sharp card image visible at the edges */}
                  <div className="absolute inset-2.5 flex flex-col justify-between overflow-hidden rounded-[16px] border border-white/25 bg-white/10 p-4 backdrop-blur-2xl">
                    <div className="pointer-events-none absolute inset-0 mix-blend-screen" style={{ background: "linear-gradient(135deg, rgba(255,255,255,0.55) 0%, rgba(255,255,255,0.14) 35%, transparent 60%)" }} />
                    <div className="pointer-events-none absolute inset-x-0 bottom-0 h-24 bg-gradient-to-t from-black/25 to-transparent" />

                    <div className="relative flex items-center justify-between">
                      <span className="text-xl font-black tracking-[-0.03em]">{step.number}</span>
                      <span className="flex h-8 w-8 items-center justify-center rounded-full border border-white/30 bg-white/15"><ArrowUpRight className="h-3.5 w-3.5" /></span>
                    </div>

                    <div className="relative">
                      <h3 className="text-sm font-bold tracking-[-0.02em]">{step.title}</h3>
                      <p className="mt-2 text-[11px] leading-relaxed text-white/80">{step.description}</p>
                    </div>
                  </div>
                </motion.article>
              );
            })}
          </div>
        </div>
      </section>

      <section className="bg-[#f6f5f1] px-4 py-24 text-[#11150e] sm:px-6 lg:px-8">
        <div className="mx-auto grid max-w-6xl items-center gap-10 lg:grid-cols-[1fr_420px]">
          <motion.div initial={{ opacity: 0, x: -50 }} whileInView={{ opacity: 1, x: 0 }} viewport={{ once: true, amount: 0.3 }} transition={{ duration: 0.65, ease: "easeOut" }}>
            <div className="mb-5 text-[10px] font-semibold uppercase tracking-[0.2em] text-[#78a43a]">Una operación más inteligente</div>
            <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">
              Organiza la venta conversacional
              <span className="block italic text-[#75a832]">en un solo espacio</span>
            </h2>
            <p className="mt-6 max-w-lg text-sm leading-relaxed text-[#697066]">VANTA responde conversaciones, consulta tu catálogo y crea pedidos con supervisión humana desde un único espacio para WhatsApp.</p>
            <Link href="/register" className="mt-6 inline-flex items-center gap-2 rounded-full bg-[#11150e] px-6 py-3 text-xs font-bold text-white shadow-[0_10px_20px_rgba(17,21,14,0.18)] transition-transform hover:-translate-y-0.5">Crear cuenta <ArrowRight className="h-3.5 w-3.5" /></Link>
          </motion.div>
          <div className="flex justify-center lg:justify-end">
          <motion.button
            type="button"
            aria-expanded={isVantaRadarOpen}
            onClick={() => setIsVantaRadarOpen((current) => !current)}
            initial={{ opacity: 0, y: 24 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true, amount: 0.3 }}
            transition={{ duration: 0.7, ease: "easeOut" }}
            className="group relative block h-[330px] w-full max-w-[390px] cursor-pointer overflow-hidden rounded-[28px] border border-white/80 bg-[#11150e] text-left shadow-[0_28px_70px_rgba(25,35,19,0.2)] focus-visible:outline-none focus-visible:ring-4 focus-visible:ring-[#8bcf32]/50"
          >
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_50%_38%,rgba(143,207,50,0.28),transparent_28%),linear-gradient(145deg,#252c20_0%,#11150e_55%,#070906_100%)]" />

            <AnimatePresence mode="wait">
              {!isVantaRadarOpen ? (
                <motion.div key="brand" initial={{ opacity: 0, scale: 0.96 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0, scale: 1.04 }} transition={{ duration: 0.35 }} className="absolute inset-0 flex flex-col items-center justify-center text-center text-[#f4f2e9]">
                  <span className="flex h-16 w-16 items-center justify-center rounded-[20px] border border-[#9ccb4f]/60 bg-[#26351b] text-3xl font-black shadow-[0_0_40px_rgba(139,207,50,0.3)]">V</span>
                  <span className="mt-4 text-xl font-semibold tracking-[-0.05em]">VANTA AI</span>
                  <span className="mt-2 max-w-xs text-xs text-[#cbd8bc]">Toca para explorar tu catálogo inteligente</span>
                  <span className="mt-7 inline-flex items-center gap-3 rounded-full border border-white/10 bg-[#080b08] py-1.5 pl-4 pr-1.5 text-[10px] font-semibold text-white shadow-[0_8px_18px_rgba(0,0,0,0.24)]">
                    Explorar productos
                    <span className="flex h-7 w-7 items-center justify-center rounded-full bg-white text-[#11150e] shadow-[0_2px_8px_rgba(255,255,255,0.2)]">
                      <ArrowUpRight className="h-3.5 w-3.5" />
                    </span>
                  </span>
                </motion.div>
              ) : (
                <motion.div key="radar" initial={{ opacity: 0, scale: 0.92 }} animate={{ opacity: 1, scale: 1 }} exit={{ opacity: 0, scale: 1.04 }} transition={{ duration: 0.45, ease: "easeOut" }} className="absolute inset-0 text-[#f4f2e9]">
                  <div className="absolute left-6 top-5 text-left"><div className="text-sm font-bold">Catálogo de la tienda</div><div className="mt-1 text-[10px] text-[#b9c9aa]">Vista ilustrativa · no conectada a un catálogo</div></div>
                  <div className="absolute left-1/2 top-1/2 h-64 w-64 -translate-x-1/2 -translate-y-1/2 rounded-full border border-white/15" />
                  <div className="absolute left-1/2 top-1/2 h-44 w-44 -translate-x-1/2 -translate-y-1/2 rounded-full border border-white/20" />
                  <div className="absolute left-1/2 top-1/2 h-24 w-24 -translate-x-1/2 -translate-y-1/2 rounded-full border border-[#9ccb4f]/60 bg-[#9ccb4f]/10 shadow-[0_0_40px_rgba(156,203,79,0.35)]" />
                  <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 rounded-full border border-[#9ccb4f]/50 bg-[#11150e] px-4 py-2 text-[9px] font-semibold text-[#dceabf]">Conectá tu catálogo</div>
                  <div className="absolute bottom-5 left-6 text-[10px] text-[#b9c9aa]">Vista conceptual · sin consulta de datos reales</div>
                  <span className="absolute bottom-4 right-5 rounded-full bg-[#f4f2e9] px-4 py-2 text-[10px] font-bold text-[#17200f]">Volver</span>
                </motion.div>
              )}
            </AnimatePresence>
          </motion.button>
            </div>
        </div>
      </section>

      <section className="bg-[#f6f5f1] px-4 py-20 text-center text-[#11150e] sm:px-6 lg:px-8">
        <motion.div initial={{ opacity: 0, y: 20 }} whileInView={{ opacity: 1, y: 0 }} viewport={{ once: true, amount: 0.4 }} transition={{ duration: 0.6 }} className="mx-auto max-w-3xl">
          <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Inteligencia comercial completa,<br /><span className="italic text-[#75a832]">un solo panel de control</span></h2>
          <p className="mx-auto mt-5 max-w-xl text-sm leading-relaxed text-[#697066]">Conversaciones, pedidos, estados de pago y actividad de tu tenant en un mismo panel.</p>
        </motion.div>
      </section>

      <section id="validacion" className="bg-[#f6f5f1] px-4 py-24 text-[#11150e] sm:px-6 lg:px-8">
        <div className="mx-auto max-w-6xl">
          <div className="mx-auto max-w-3xl text-center">
            <p className="text-[10px] font-semibold uppercase tracking-[0.2em] text-[#78a43a]">Etapa de validación</p>
            <h2 className="mt-4 text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Construyamos el piloto<br /><span className="italic text-[#75a832]">con comercios reales</span></h2>
            <p className="mt-5 text-sm leading-relaxed text-[#697066]">Estamos validando el flujo de ventas, la atención humana y la transferencia manual con design partners. Aún no publicamos testimonios ni resultados de clientes.</p>
          </div>
        </div>
      </section>

      <section id="precios" className="relative z-10 overflow-hidden bg-[#f6f5f1] px-4 py-24 text-[#11150e] sm:px-6 lg:px-8">
        <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(#c7cabd_1px,transparent_1px)] [background-size:28px_28px] opacity-60" />
        <div className="relative z-10 mx-auto max-w-6xl">
          <div className="mx-auto max-w-2xl text-center">
            <div className="mb-5 inline-flex items-center gap-2 rounded-full border border-[#dfe2d9] bg-white px-3 py-1.5 text-[10px] font-semibold text-[#66705d]">
              <span className="h-2 w-2 rounded-full bg-[#8bcf32]" /> Pricing en validación
            </div>
            <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Un modelo alineado<br /><span className="italic text-[#75a832]">al valor para tu tienda</span></h2>
            <p className="mt-5 text-sm leading-relaxed text-[#697066]">Estamos conversando con comercios para definir planes, uso incluido y soporte. Todavía no hay tarifas ni tiers aprobados.</p>
          </div>

          <div className="mx-auto mt-10 max-w-3xl border-y border-[#dfe2d9] py-6 text-center text-sm text-[#697066]">
            Los costos dependerán de uso, proveedores y soporte. No publicaremos cifras hasta contar con evidencia de pilotos y un modelo de margen validado.
          </div>
        </div>
      </section>

      <section id="faq" className="relative z-10 overflow-hidden bg-[#11150e] px-4 py-24 text-[#f4f2e9] sm:px-6 lg:px-8">
        <div className="mx-auto max-w-5xl">
          <div className="mx-auto max-w-2xl text-center">
            <div className="mb-5 text-[10px] font-semibold uppercase tracking-[0.22em] text-[#9ccb4f]">Preguntas frecuentes</div>
            <h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Todo lo que necesitas <span className="italic text-[#9ccb4f]">saber</span></h2>
            <p className="mt-5 text-sm text-[#b9c0ad]">VANTA se encarga de responder, vender y organizar. Tú mantienes el control.</p>
          </div>
          <div className="mx-auto mt-14 max-w-3xl divide-y divide-white/10 border-y border-white/10">
            {FAQS.slice(0, 4).map((faq, index) => {
              const isOpen = openQuestion === index;
              return <div key={faq.q} className="py-5"><button onClick={() => setOpenQuestion(isOpen ? -1 : index)} className="flex w-full items-center justify-between gap-5 text-left"><span className="text-sm font-semibold sm:text-base">{faq.q}</span><span className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-white/20 text-lg text-[#9ccb4f] transition-transform ${isOpen ? "rotate-45" : ""}`}>+</span></button><AnimatePresence>{isOpen && <motion.p initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="max-w-2xl overflow-hidden pr-12 pt-4 text-xs leading-relaxed text-[#b9c0ad] sm:text-sm">{faq.a}</motion.p>}</AnimatePresence></div>;
            })}
          </div>
        </div>
      </section>

      <section className="relative z-10 overflow-hidden bg-[#11150e] px-4 pb-28 pt-12 text-center text-[#f4f2e9] sm:px-6 lg:px-8">
        <div className="relative mx-auto max-w-6xl overflow-hidden rounded-[28px] border border-[#7e9f43]/30 bg-[radial-gradient(ellipse_at_center,_#9ccb4f_0%,_#354b1c_35%,_#11150e_73%)] px-6 py-24 shadow-[0_0_100px_rgba(145,201,54,0.24)] sm:py-32">
          <div className="relative z-10 mx-auto max-w-2xl"><div className="mb-5 text-[10px] font-semibold uppercase tracking-[0.22em] text-[#dceabf]">Comercio conversacional</div><h2 className="text-4xl font-semibold leading-[0.95] tracking-[-0.07em] sm:text-6xl">Organiza tus ventas<br /><span className="italic text-[#dceabf]">desde las conversaciones</span></h2><p className="mx-auto mt-5 max-w-md text-sm text-[#e0e9d0]/80">Consulta catálogo, registra pedidos y conserva el control de cada pago y conversación.</p><div className="mt-8 flex flex-col justify-center gap-3 sm:flex-row"><Link href="/register" className="rounded-full bg-[#f4f2e9] px-6 py-3 text-xs font-bold text-[#17200f] transition-transform hover:-translate-y-0.5">Crear cuenta</Link><Link href="/login" className="rounded-full border border-white/30 px-6 py-3 text-xs font-bold text-white transition-colors hover:bg-white/10">Ingresar al panel</Link></div></div>
        </div>
      </section>

      <footer className="relative z-10 bg-[#11150e] px-4 pb-10 pt-4 text-[#aeb7a2] sm:px-6 lg:px-8"><div className="mx-auto flex max-w-6xl flex-col justify-between gap-5 border-t border-white/10 pt-6 text-[10px] sm:flex-row"><div><span className="font-bold text-white">VANTA</span><span className="ml-3">Revenue infrastructure para WhatsApp</span></div><div className="flex gap-5"><span>Privacidad</span><span>Condiciones</span><span>© 2026 VANTA</span></div></div></footer>
    </>
  );
}

export default function Home() {
  return (
    <main className="vanta-landing min-h-screen overflow-x-hidden bg-[#f6f5f1] font-sans text-[#171b14] antialiased selection:bg-[#b8e45c] selection:text-[#17200f]">
      <ReferenceHero />
      <ReferenceProductSections />
    </main>
  );
}

