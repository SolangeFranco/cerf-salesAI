"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { EmptyState } from "@/components/ui/empty-state";
import { Skeleton } from "@/components/ui/skeleton";
import { Search, Plus, Phone, Mail, MapPin, Edit2, Trash2, Eye, Users, LayoutGrid, TableProperties, ArrowUpRight } from "lucide-react";

type Customer = {
  id: string;
  name: string;
  phone: string;
  email: string | null;
  address: string | null;
  createdAt: string;
};

const avatarPalette = ["#f6d1c5", "#c9e4ff", "#d6ecc9", "#e6d6ff", "#ffe1b3", "#c9f0e2"];

const avatarColor = (seed: string) => avatarPalette[seed.charCodeAt(0) % avatarPalette.length];

const initials = (name: string) =>
  name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("") || "?";

export function CustomerPanel() {
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [selectedCustomerId, setSelectedCustomerId] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [isLoading, setIsLoading] = useState(true);
  const [mode, setMode] = useState<"list" | "create">("list");
  const [viewMode, setViewMode] = useState<"table" | "dash">("table");
  const [formValues, setFormValues] = useState({ name: "", phone: "", email: "", address: "" });
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadCustomers = async () => {
    setIsLoading(true);
    const response = await fetch("/api/customers", { cache: "no-store" });
    if (!response.ok) {
      setIsLoading(false);
      return;
    }
    setCustomers((await response.json()) as Customer[]);
    setIsLoading(false);
  };

  useEffect(() => {
    void Promise.resolve().then(loadCustomers);
  }, []);

  const selectedCustomer = customers.find((c) => c.id === selectedCustomerId);
  const filteredCustomers = customers.filter(
    (c) =>
      c.name.toLowerCase().includes(search.toLowerCase()) ||
      c.phone.includes(search) ||
      (c.email ?? "").toLowerCase().includes(search.toLowerCase())
  );

  const handleCreate = () => {
    setMode("create");
    setFormValues({ name: "", phone: "", email: "", address: "" });
    setError(null);
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);
    setIsSubmitting(true);
    try {
      const response = await fetch("/api/customers", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(formValues),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        setError(data?.error ?? "No se pudo crear el cliente.");
        return;
      }
      setFormValues({ name: "", phone: "", email: "", address: "" });
      await loadCustomers();
      setMode("list");
    } finally {
      setIsSubmitting(false);
    }
  };

  const createdDate = selectedCustomer
    ? new Date(selectedCustomer.createdAt).toLocaleDateString("es-PY")
    : null;

  return (
    <div className="w-full">
      {mode === "list" && (
        <section className="overflow-hidden rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] shadow-[0_12px_34px_rgba(24,22,20,0.04)]">
          <div className="flex flex-col gap-5 border-b border-[#eee8e4] px-4 pb-5 pt-2 sm:px-6 lg:px-7">
            <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
              <h1 className="text-[30px] font-semibold tracking-[-0.07em] text-[#1b1e21]">Lista de clientes</h1>
              <button onClick={handleCreate} className="inline-flex items-center gap-2 rounded-full bg-[#a7edb3] px-4 py-2.5 text-[13px] font-medium text-[#1c3a28] shadow-[0_8px_18px_rgba(97,190,112,0.14)]">
                Agregar cliente <Plus className="size-4" />
              </button>
            </div>
            <div className="flex flex-col justify-between gap-3 lg:flex-row lg:items-center">
              <div className="text-[16px] font-medium text-[#24282c]">{customers.length.toLocaleString("es-PY")} clientes</div>
              <div className="flex flex-wrap items-center gap-2">
                <label className="flex min-w-[220px] items-center gap-2 rounded-full bg-[#f1efed] px-3 py-2 text-[12px] text-[#858b90]">
                  <Search className="size-4" />
                  <Input type="text" placeholder="Buscar por nombre, teléfono o email" value={search} onChange={(e) => setSearch(e.target.value)} className="h-auto border-0 bg-transparent p-0 text-[12px] shadow-none focus-visible:ring-0" />
                </label>
                <div className="flex items-center gap-1 rounded-full bg-[#f1efed] p-1">
                  <button
                    type="button"
                    aria-label="Vista de tabla"
                    onClick={() => setViewMode("table")}
                    className={`flex size-8 items-center justify-center rounded-full transition-colors ${viewMode === "table" ? "bg-white text-[#24282c] shadow-sm" : "text-[#858b90]"}`}
                  >
                    <TableProperties className="size-4" />
                  </button>
                  <button
                    type="button"
                    aria-label="Vista de dashboard"
                    onClick={() => setViewMode("dash")}
                    className={`flex size-8 items-center justify-center rounded-full transition-colors ${viewMode === "dash" ? "bg-white text-[#24282c] shadow-sm" : "text-[#858b90]"}`}
                  >
                    <LayoutGrid className="size-4" />
                  </button>
                </div>
              </div>
            </div>
          </div>

          {isLoading && <div className="space-y-3 p-6"><Skeleton className="h-14" /><Skeleton className="h-14" /><Skeleton className="h-14" /></div>}
          {!isLoading && filteredCustomers.length === 0 && <EmptyState icon={Users} title={search ? "Sin resultados" : "Todavía no hay clientes"} description={search ? "Probá con otro nombre, teléfono o email." : "Agregá tu primer cliente para empezar a registrar contactos."} className="min-h-44 border-0 bg-transparent px-6 py-8" />}

          {!isLoading && filteredCustomers.length > 0 && viewMode === "dash" && (
            <div className="grid grid-cols-1 gap-4 p-4 sm:grid-cols-2 sm:p-6 lg:grid-cols-3 lg:p-7">
              {filteredCustomers.map((customer) => (
                <div key={customer.id} className="flex flex-col gap-4 rounded-[18px] border border-[#eee9e5] bg-white p-4 shadow-sm">
                  <div className="flex items-center gap-3">
                    <span
                      className="flex size-10 shrink-0 items-center justify-center rounded-full text-[13px] font-semibold text-[#3c4247]"
                      style={{ backgroundColor: avatarColor(customer.id) }}
                    >
                      {initials(customer.name)}
                    </span>
                    <div className="min-w-0">
                      <p className="truncate text-[14px] font-semibold text-[#1b1e21]">{customer.name}</p>
                      <p className="text-[11px] text-[#9a9da0]">ID #{customer.id.slice(-6).toUpperCase()}</p>
                    </div>
                  </div>

                  <div className="grid grid-cols-1 gap-2 text-[12px] text-[#4c5257]">
                    <span className="inline-flex items-center gap-1.5"><Phone className="size-3.5 text-[#9a9da0]" /> {customer.phone}</span>
                    <span className="inline-flex items-center gap-1.5"><Mail className="size-3.5 text-[#9a9da0]" /> {customer.email || "Sin email"}</span>
                    <span className="inline-flex items-center gap-1.5"><MapPin className="size-3.5 text-[#9a9da0]" /> {customer.address || "Sin dirección"}</span>
                  </div>

                  <div className="flex items-center justify-between border-t border-[#eee9e5] pt-3">
                    <span className="text-[11px] text-[#9a9da0]">Cliente desde {new Date(customer.createdAt).toLocaleDateString("es-PY")}</span>
                    <Link
                      href={`/dashboard/customers/${customer.id}`}
                      aria-label={`Ver detalle de ${customer.name}`}
                      className="flex size-8 items-center justify-center rounded-full border border-[#e5dfdc] text-[#555c62] hover:text-[#6f39cf]"
                    >
                      <ArrowUpRight className="size-3.5" />
                    </Link>
                  </div>
                </div>
              ))}
            </div>
          )}

          {!isLoading && filteredCustomers.length > 0 && viewMode === "table" && (
            <div className="overflow-x-auto">
              <table className="min-w-[850px] w-full border-separate border-spacing-0 text-left">
                <thead>
                  <tr className="text-[12px] font-semibold text-[#33383c]">
                    <th className="px-6 py-4 lg:px-7">Cliente</th>
                    <th className="px-4 py-4">Teléfono</th>
                    <th className="px-4 py-4">Email</th>
                    <th className="px-4 py-4">Dirección</th>
                    <th className="px-4 py-4">Cliente desde</th>
                    <th className="px-6 py-4 text-right lg:px-7">Acción</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredCustomers.map((customer, index) => (
                    <tr key={customer.id} onClick={() => setSelectedCustomerId(customer.id)} className={`group text-[12px] text-[#3f4549] ${index % 2 === 0 ? "bg-[#f1f1f1]" : "bg-transparent"}`}>
                      <td className={`px-6 py-4 lg:px-7 ${index === 0 ? "rounded-l-[24px]" : ""}`}>
                        <div className="flex items-center gap-3">
                          <span
                            className="flex size-9 shrink-0 items-center justify-center rounded-full text-[12px] font-semibold text-[#3c4247]"
                            style={{ backgroundColor: avatarColor(customer.id) }}
                          >
                            {initials(customer.name)}
                          </span>
                          <span className="max-w-[180px] font-medium text-[#252a2e]">{customer.name}</span>
                        </div>
                      </td>
                      <td className="px-4 py-4">{customer.phone}</td>
                      <td className="px-4 py-4">{customer.email || "—"}</td>
                      <td className="px-4 py-4 max-w-[220px] truncate">{customer.address || "—"}</td>
                      <td className="px-4 py-4">{new Date(customer.createdAt).toLocaleDateString("es-PY")}</td>
                      <td className={`px-6 py-4 text-right lg:px-7 ${index === 0 ? "rounded-r-[24px]" : ""}`}>
                        <div className="flex justify-end gap-2">
                          <Link
                            href={`/dashboard/customers/${customer.id}`}
                            aria-label={`Ver detalle de ${customer.name}`}
                            onClick={(event) => event.stopPropagation()}
                            className="flex size-8 items-center justify-center rounded-full bg-white text-[#555c62] shadow-sm hover:text-[#6f39cf]"
                          >
                            <Eye className="size-3.5" />
                          </Link>
                          <button
                            type="button"
                            disabled
                            title="Editar clientes estará disponible próximamente."
                            onClick={(event) => event.stopPropagation()}
                            className="flex size-8 items-center justify-center rounded-full bg-white text-[#555c62] opacity-50 shadow-sm"
                          >
                            <Edit2 className="size-3.5" />
                          </button>
                          <button
                            type="button"
                            disabled
                            title="Eliminar clientes estará disponible próximamente."
                            onClick={(event) => event.stopPropagation()}
                            className="flex size-8 items-center justify-center rounded-full bg-[#ffd0d0] text-[#a44343] opacity-50 shadow-sm"
                          >
                            <Trash2 className="size-3.5" />
                          </button>
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {mode === "create" && (
        <section className="overflow-hidden rounded-[24px] border border-[#e8e2de] bg-[#fbfaf9] p-4 shadow-[0_12px_34px_rgba(24,22,20,0.04)] sm:p-6 lg:p-7">
          <button onClick={() => setMode("list")} className="mb-4 text-left text-xs text-[#737a80] hover:text-[#24282c]">← Volver a clientes</button>
          <h1 className="text-[25px] font-semibold tracking-[-0.06em] text-[#1b1e21]">Nuevo cliente</h1>
          <form onSubmit={handleSubmit} className="mt-5 grid max-w-lg gap-4">
            <div className="flex flex-col gap-2">
              <Label htmlFor="customer-name" className="text-[12px] font-medium text-[#3d4449]">Nombre</Label>
              <Input id="customer-name" value={formValues.name} onChange={(e) => setFormValues({ ...formValues, name: e.target.value })} placeholder="Juan Pérez" required className="h-11 rounded-[11px] border-0 bg-[#f0f0f0] px-3 text-[13px] shadow-none" />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="customer-phone" className="text-[12px] font-medium text-[#3d4449]">Teléfono</Label>
              <Input id="customer-phone" value={formValues.phone} onChange={(e) => setFormValues({ ...formValues, phone: e.target.value })} placeholder="+595981234567" required className="h-11 rounded-[11px] border-0 bg-[#f0f0f0] px-3 text-[13px] shadow-none" />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="customer-email" className="text-[12px] font-medium text-[#3d4449]">Email <span className="text-[#9a9da0]">(opcional)</span></Label>
              <Input id="customer-email" type="email" value={formValues.email} onChange={(e) => setFormValues({ ...formValues, email: e.target.value })} placeholder="juan@example.com" className="h-11 rounded-[11px] border-0 bg-[#f0f0f0] px-3 text-[13px] shadow-none" />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="customer-address" className="text-[12px] font-medium text-[#3d4449]">Dirección <span className="text-[#9a9da0]">(opcional)</span></Label>
              <Input id="customer-address" value={formValues.address} onChange={(e) => setFormValues({ ...formValues, address: e.target.value })} placeholder="Calle Principal 123, Asunción" className="h-11 rounded-[11px] border-0 bg-[#f0f0f0] px-3 text-[13px] shadow-none" />
            </div>

            {error && <p className="rounded-[10px] bg-[#fff0f0] px-3 py-2 text-sm text-[#b55757]">{error}</p>}

            <button type="submit" disabled={isSubmitting} className="mt-2 inline-flex items-center justify-center gap-2 rounded-full bg-[#a7edb3] px-4 py-2.5 text-[13px] font-medium text-[#1c3a28] shadow-[0_8px_18px_rgba(97,190,112,0.14)] disabled:opacity-50">
              {isSubmitting ? "Guardando..." : "Agregar cliente"}
            </button>
          </form>
        </section>
      )}

      {mode === "list" && selectedCustomer && (
        <div className="mt-4 flex flex-col gap-3 rounded-[20px] border border-[#e8e2de] bg-[#fbfaf9] p-4 shadow-sm sm:flex-row sm:items-start sm:justify-between">
          <div>
            <p className="text-sm font-semibold text-[#24282c]">{selectedCustomer.name}</p>
            <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
              <span className="inline-flex items-center gap-1"><Phone className="size-3" /> {selectedCustomer.phone}</span>
              {selectedCustomer.email && <span className="inline-flex items-center gap-1"><Mail className="size-3" /> {selectedCustomer.email}</span>}
              {selectedCustomer.address && <span className="inline-flex items-center gap-1"><MapPin className="size-3" /> {selectedCustomer.address}</span>}
            </p>
            <p className="mt-1 text-xs text-muted-foreground">Cliente desde {createdDate}</p>
          </div>
        </div>
      )}
    </div>
  );
}

