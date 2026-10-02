"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { LoaderCircle, Search } from "lucide-react";

type CustomerResult = {
  id: string;
  name: string;
  phone: string;
  email: string | null;
};

type ProductResult = {
  id: string;
  name: string;
  description: string | null;
  sku: string;
  category?: string | null;
};

type OrderResult = {
  id: string;
  number: string;
  status: string;
  customerId: string | null;
  items: { productName: string }[];
};

type SearchResult = {
  id: string;
  label: string;
  detail: string;
  href: string;
};

type SearchGroup = {
  key: "customers" | "products" | "orders";
  label: string;
  results: SearchResult[];
};

const statusLabels: Record<string, string> = {
  New: "Nuevo",
  Confirmed: "Confirmado",
  AwaitingPayment: "Esperando pago",
  Paid: "Pagado",
  Preparing: "Preparando",
  Shipped: "Enviado",
  Delivered: "Entregado",
  Cancelled: "Cancelado",
  Refunded: "Reembolsado",
  PaymentFailed: "Pago rechazado",
};

function normalize(value: string | null | undefined) {
  return (value ?? "").trim().toLocaleLowerCase();
}

function modulePriority(pathname: string): SearchGroup["key"][] {
  if (pathname.startsWith("/dashboard/customers")) return ["customers", "orders", "products"];
  if (pathname.startsWith("/dashboard/products")) return ["products", "customers", "orders"];
  if (pathname.startsWith("/dashboard/orders")) return ["orders", "customers", "products"];
  return ["customers", "products", "orders"];
}

function matches(query: string, values: (string | null | undefined)[]) {
  return values.some((value) => normalize(value).includes(query));
}

export function GlobalSearch() {
  const pathname = usePathname();
  const containerRef = useRef<HTMLDivElement>(null);
  const [query, setQuery] = useState("");
  const [isOpen, setIsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [customers, setCustomers] = useState<CustomerResult[]>([]);
  const [products, setProducts] = useState<ProductResult[]>([]);
  const [orders, setOrders] = useState<OrderResult[]>([]);
  const [hasLoaded, setHasLoaded] = useState(false);

  useEffect(() => {
    const handlePointerDown = (event: PointerEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) setIsOpen(false);
    };
    const handleKeyDown = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        containerRef.current?.querySelector<HTMLInputElement>("input")?.focus();
        setIsOpen(true);
      }
      if (event.key === "Escape") setIsOpen(false);
    };

    document.addEventListener("pointerdown", handlePointerDown);
    document.addEventListener("keydown", handleKeyDown);
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown);
      document.removeEventListener("keydown", handleKeyDown);
    };
  }, []);

  const loadSearchData = async () => {
    if (hasLoaded || isLoading) return;
    setIsLoading(true);
    try {
      const responses = await Promise.all([
        fetch("/api/customers", { cache: "no-store" }),
        fetch("/api/products", { cache: "no-store" }),
        fetch("/api/orders", { cache: "no-store" }),
      ]);
      if (responses.every((response) => response.ok)) {
        const [customerData, productData, orderData] = await Promise.all(
          responses.map((response) => response.json()),
        );
        setCustomers(customerData as CustomerResult[]);
        setProducts(productData as ProductResult[]);
        setOrders(orderData as OrderResult[]);
        setHasLoaded(true);
      }
    } finally {
      setIsLoading(false);
    }
  };

  const searchQuery = normalize(query);
  const groupsByKey: Record<SearchGroup["key"], SearchGroup> = {
    customers: {
      key: "customers",
      label: "Clientes",
      results: customers
        .filter((customer) => matches(searchQuery, [customer.name, customer.phone, customer.email]))
        .slice(0, 5)
        .map((customer) => ({
          id: customer.id,
          label: customer.name,
          detail: customer.phone,
          href: `/dashboard/customers/${customer.id}`,
        })),
    },
    products: {
      key: "products",
      label: "Productos",
      results: products
        .filter((product) => matches(searchQuery, [product.name, product.description, product.category, product.sku]))
        .slice(0, 5)
        .map((product) => ({
          id: product.id,
          label: product.name,
          detail: product.category || product.sku,
          href: `/dashboard/products/${product.id}`,
        })),
    },
    orders: {
      key: "orders",
      label: "Pedidos",
      results: orders
        .filter((order) => matches(searchQuery, [order.number, statusLabels[order.status] ?? order.status, ...order.items.map((item) => item.productName)]))
        .slice(0, 5)
        .map((order) => ({
          id: order.id,
          label: order.number,
          detail: statusLabels[order.status] ?? order.status,
          href: `/dashboard/orders/${order.id}`,
        })),
    },
  };
  const groups = modulePriority(pathname).map((key) => groupsByKey[key]);
  const resultCount = groups.reduce((count, group) => count + group.results.length, 0);
  const showResults = isOpen && searchQuery.length > 0;

  return (
    <div ref={containerRef} className="relative col-start-2 w-[min(420px,42vw)] min-w-[180px]">
      <label className="flex w-full items-center gap-2 rounded-full border border-[#e7e0dc] bg-[#f1efed] px-3.5 py-2.5 text-[#858b90] shadow-[inset_0_1px_0_rgba(255,255,255,0.8)]">
        {isLoading ? <LoaderCircle className="size-4 shrink-0 animate-spin" /> : <Search className="size-4 shrink-0" />}
        <input
          aria-label="Buscar clientes, productos o pedidos"
          placeholder="Buscar clientes, productos o pedidos"
          value={query}
          onFocus={() => {
            setIsOpen(true);
            void loadSearchData();
          }}
          onChange={(event) => {
            setQuery(event.target.value);
            setIsOpen(true);
          }}
          className="min-w-0 flex-1 bg-transparent text-[13px] text-[#30353a] outline-none placeholder:text-[#858b90]"
        />
        <span className="hidden rounded-[6px] border border-[#ddd6d2] bg-[#f7f5f3] px-1.5 py-0.5 text-[10px] font-semibold text-[#777e84] sm:inline">⌘ K</span>
      </label>

      {showResults && (
        <div className="absolute left-0 right-0 top-[calc(100%+8px)] z-50 max-h-[min(480px,70vh)] overflow-y-auto rounded-[16px] border border-[#e6dfdb] bg-[#fffdfb] p-2 shadow-[0_18px_45px_rgba(45,34,28,0.16)]">
          {isLoading ? (
            <p className="px-3 py-5 text-center text-xs text-[#7a8187]">Buscando...</p>
          ) : resultCount === 0 ? (
            <p className="px-3 py-5 text-center text-xs text-[#7a8187]">No encontramos resultados.</p>
          ) : (
            groups.map((group) => group.results.length > 0 && (
              <section key={group.key} className="mb-2 last:mb-0">
                <h2 className="px-3 pb-1 pt-2 text-[10px] font-semibold uppercase tracking-[0.12em] text-[#95999d]">{group.label}</h2>
                {group.results.map((result) => (
                  <Link
                    key={result.id}
                    href={result.href}
                    onClick={() => setIsOpen(false)}
                    className="flex items-center justify-between gap-3 rounded-[10px] px-3 py-2.5 transition hover:bg-[#f3efec]"
                  >
                    <span className="min-w-0 truncate text-[13px] font-medium text-[#30353a]">{result.label}</span>
                    <span className="shrink-0 truncate text-right text-[11px] text-[#858b90]">{result.detail}</span>
                  </Link>
                ))}
              </section>
            ))
          )}
        </div>
      )}
    </div>
  );
}
