import { redirect } from "next/navigation";
import { getCurrentTenant } from "@/lib/api-server";
import { WhatsAppConnectionOptions } from "@/components/onboarding/whatsapp-connection-options";

export default async function WhatsAppOnboardingPage() {
  const tenant = await getCurrentTenant();
  if (!tenant) redirect("/login");
  if (tenant.storeSetupCompleted) redirect("/dashboard");

  return (
    <main className="min-h-screen bg-[#f7f5f1] px-5 py-8 text-[#202526] sm:px-10 lg:px-16 lg:py-12">
      <header className="mx-auto mb-14 flex max-w-5xl items-center gap-2.5">
        <span className="flex size-9 items-center justify-center rounded-[11px] bg-[#292d32] text-xs font-black text-white">V</span>
        <span className="text-xl font-black tracking-[-0.05em]">VANTA<span className="text-[#159447]">.</span></span>
      </header>
      <div className="mx-auto max-w-5xl">
        <WhatsAppConnectionOptions tenantName={tenant.name} hasWhatsAppChannel={tenant.hasWhatsAppChannel} />
      </div>
    </main>
  );
}
