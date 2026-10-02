import { test, expect } from "@playwright/test";
import { createE2EStore, signInE2EMerchant } from "./fixtures";

const API_BASE_URL = "http://localhost:5052";

test("merchant requests and confirms a bank transfer only after verification", async ({ page }) => {
  const store = await createE2EStore(page, "Producto Transferencia E2E");
  const authorization = { Authorization: `Bearer ${store.accessToken}` };
  const createOrder = await page.request.post(`${API_BASE_URL}/api/orders`, {
    headers: { ...authorization, "Idempotency-Key": `e2e-manual-order-${store.suffix}` },
    data: { items: [{ productId: store.productId, quantity: 1 }] },
  });
  expect(createOrder.ok(), "test order should be created").toBeTruthy();
  const order = (await createOrder.json()) as { id: string };

  const confirmOrder = await page.request.patch(`${API_BASE_URL}/api/orders/${order.id}/status`, {
    headers: authorization,
    data: { status: "Confirmed" },
  });
  expect(confirmOrder.ok(), "test order should be confirmed before payment").toBeTruthy();

  await signInE2EMerchant(page, store);
  await page.goto(`/dashboard/orders/${order.id}`);
  await page.getByRole("button", { name: "Solicitar transferencia" }).click();

  await expect(page.getByText("Banco: Banco E2E")).toBeVisible();
  await expect(page.getByText("Beneficiario: Comercio de prueba")).toBeVisible();
  await expect(page.getByText("Cuenta: CTA-").first()).toBeVisible();
  await expect(page.getByText("Documento: 80000000-0")).toBeVisible();
  await expect(page.getByText("Estado").locator(".." ).getByText("Pendiente")).toBeVisible();

  const pendingDetail = await page.request.get(`${API_BASE_URL}/api/orders/${order.id}`, { headers: authorization });
  expect(pendingDetail.ok()).toBeTruthy();
  const pending = (await pendingDetail.json()) as { status: string; payment: { status: string; provider: string } };
  expect(pending.status).toBe("AwaitingPayment");
  expect(pending.payment.status).toBe("Pending");
  expect(pending.payment.provider).toBe("manual-transfer");

  await page.getByRole("button", { name: "Marcar transferencia como pagada" }).click();
  await expect.poll(async () => {
    const response = await page.request.get(`${API_BASE_URL}/api/orders/${order.id}`, { headers: authorization });
    if (!response.ok()) return "request-failed";
    const detail = (await response.json()) as { status: string; payment: { status: string } };
    return `${detail.status}:${detail.payment.status}`;
  }).toBe("Paid:Paid");
});
