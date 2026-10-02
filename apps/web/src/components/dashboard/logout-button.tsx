"use client";

import { useRouter } from "next/navigation";
import { Button } from "@/components/ui/button";
import { LogOut } from "lucide-react";

export function LogoutButton() {
  const router = useRouter();

  const handleLogout = async () => {
    await fetch("/api/auth/logout", { method: "POST" });
    router.push("/login");
    router.refresh();
  };

  return (
    <Button variant="ghost" size="icon" onClick={handleLogout} title="Cerrar sesión" aria-label="Cerrar sesión" className="size-10 rounded-full text-[#66705d] hover:bg-[#f1ead1] hover:text-[#11150e]">
      <LogOut className="size-4" />
    </Button>
  );
}
