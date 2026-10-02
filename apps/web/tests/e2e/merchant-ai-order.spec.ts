import { test, expect } from "@playwright/test";
import { createE2EConversation, createE2EStore, signInE2EMerchant } from "./fixtures";

const API_BASE_URL = "http://localhost:5052";

test("merchant creates a product, an AI conversation recommends it, and a purchase message creates an order", async ({ page }) => {
  const store = await createE2EStore(page, "Zapatilla Venta E2E");
  const conversation = await createE2EConversation(page, store, store.productName);

  await signInE2EMerchant(page, store);
  await page.goto("/dashboard/conversations");
  await page.getByRole("link", { name: conversation.customerExternalId }).click();
  await page.getByRole("button", { name: "Responder con agente" }).click();
  await expect(page.getByText(new RegExp(store.productName)).first()).toBeVisible();

  const suffix = store.suffix;
  const headers = { "X-Webhook-Signature": "mock-dev" };
  for (const [event, text] of [
    ["purchase", `Quiero comprar 1 unidad de ${store.productName}`],
    ["confirm", "Sí, confirmo"],
  ]) {
    const response = await page.request.post(`${API_BASE_URL}/api/webhooks/whatsapp/mock`, {
      headers,
      data: {
        eventId: `e2e-${event}-${suffix}`,
        accountExternalId: store.accountExternalId,
        customerExternalId: conversation.customerExternalId,
        messageId: `e2e-${event}-message-${suffix}`,
        text,
      },
    });
    expect(response.ok(), `${event} message should be accepted`).toBeTruthy();
  }

  const ordersResponse = await page.request.get(`${API_BASE_URL}/api/orders`, {
    headers: { Authorization: `Bearer ${store.accessToken}` },
  });
  expect(ordersResponse.ok()).toBeTruthy();
  const orders = (await ordersResponse.json()) as { number: string; status: string; items: { productName: string }[] }[];
  expect(orders).toHaveLength(1);
  expect(orders[0].status).toBe("AwaitingPayment");
  expect(orders[0].items.some((item) => item.productName === store.productName)).toBeTruthy();

  await page.goto("/dashboard/orders");
  await page.getByRole("link", { name: "Ver pedido" }).first().click();
  await expect(page.getByText(store.productName)).toBeVisible({ timeout: 10_000 });
});
