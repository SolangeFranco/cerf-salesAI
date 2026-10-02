import { z } from "zod";

export const registerSchema = z.object({
  tenantName: z.string().min(2, "Ingresá el nombre de tu tienda."),
  email: z.email("Ingresá un email válido."),
  password: z
    .string()
    .min(8, "La contraseña debe tener al menos 8 caracteres."),
});

export type RegisterInput = z.infer<typeof registerSchema>;

export const loginSchema = z.object({
  email: z.email("Ingresá un email válido."),
  password: z.string().min(1, "Ingresá tu contraseña."),
});

export type LoginInput = z.infer<typeof loginSchema>;
