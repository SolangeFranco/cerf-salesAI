"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { LoaderCircle } from "lucide-react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { loginSchema, type LoginInput } from "@/lib/validation/auth";

export function LoginForm() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [serverError, setServerError] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginInput>({ resolver: zodResolver(loginSchema) });

  const onSubmit = async (values: LoginInput) => {
    setServerError(null);
    let response: Response;
    try {
      response = await fetch("/api/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(values),
      });
    } catch {
      setServerError("No se pudo conectar con VANTA. Revisá tu conexión e intentá de nuevo.");
      return;
    }

    if (!response.ok) {
      const data = await response.json().catch(() => null);
      setServerError(data?.error ?? "No se pudo iniciar sesión.");
      return;
    }

    router.push(searchParams.get("from") ?? "/dashboard");
    router.refresh();
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-5">
      <div className="flex flex-col gap-2">
        <Label className="text-xs font-bold uppercase tracking-[0.12em] text-[#65736d]" htmlFor="email">
          Email
        </Label>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          placeholder="vos@tutienda.com"
          className="h-12 rounded-xl border-[#cbded0] bg-white px-4 text-[#202526] placeholder:text-[#9aa9a0] focus-visible:border-[#159447] focus-visible:ring-1 focus-visible:ring-[#159447]"
          {...register("email")}
        />
        {errors.email && <p className="text-xs font-medium text-red-500">{errors.email.message}</p>}
      </div>

      <div className="flex flex-col gap-2">
        <Label className="text-xs font-bold uppercase tracking-[0.12em] text-[#65736d]" htmlFor="password">
          Contraseña
        </Label>
        <Input
          id="password"
          type="password"
          autoComplete="current-password"
          placeholder="••••••••"
          className="h-12 rounded-xl border-[#cbded0] bg-white px-4 text-[#202526] placeholder:text-[#9aa9a0] focus-visible:border-[#159447] focus-visible:ring-1 focus-visible:ring-[#159447]"
          {...register("password")}
        />
        {errors.password && <p className="text-xs font-medium text-red-500">{errors.password.message}</p>}
      </div>

      {serverError && (
        <div role="alert" className="rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-xs font-semibold text-red-700">
          {serverError}
        </div>
      )}

      <Button
        type="submit"
        size="lg"
        disabled={isSubmitting}
        className="h-12 rounded-xl bg-[#159447] font-bold text-white transition-all hover:bg-[#117b3a] active:scale-[0.99]"
      >
        {isSubmitting ? <><LoaderCircle className="size-4 animate-spin" /> Ingresando...</> : "Ingresar"}
      </Button>
    </form>
  );
}
