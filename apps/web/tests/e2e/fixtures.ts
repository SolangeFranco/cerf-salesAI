import { randomUUID } from "node:crypto";
import { expect, type Page } from "@playwright/test";

const API_BASE_URL = "http://localhost:5052";
const E2E_PASSWORD = "Passw0rd!123";

export type E2EStore = {
  tenantName: string;
  email: string;
  password: string;
  accessToken: string;
  slug: string;
  accountExternalId: string;
  productId: string;
  productName: string;
  suffix: string;
};

export async function createE2EStore(page: Page, requestedProductName = "Producto E2E"): Promise<E2EStore> {
  const suffix = randomUUID().replaceAll("-", "");
  const tenantName = `Merchant E2E ${suffix}`;
  const email = `merchant-e2e-${suffix}@vanta.local`;
  const accountExternalId = `mock-e2e-${suffix}`;
  const productName = requestedProductName;

  const registration = await page.request.post(`${API_BASE_URL}/api/auth/register`, {
    data: { tenantName, email, password: E2E_PASSWORD },
  });
  expect(registration.ok(), "merchant registration should succeed").toBeTruthy();
  const auth = (await registration.json()) as { accessToken: string };
  const authorization = { Authorization: `Bearer ${auth.accessToken}` };

  const channel = await page.request.post(`${API_BASE_URL}/api/channels/whatsapp/mock`, {
    headers: authorization,
    data: { externalAccountId: accountExternalId, displayName: "E2E Mock Channel" },
  });
  expect(channel.ok(), "mock WhatsApp channel should connect").toBeTruthy();

  const settings = await page.request.put(`${API_BASE_URL}/api/tenants/settings`, {
    headers: authorization,
    data: {
      name: tenantName,
      deliveryBaseCostGs: 0,
      deliveryIncludedDistanceKm: 1,
      deliveryExtraCostPerKmGs: 0,
      metropolitanDeliveryCostGs: 0,
      interiorDeliveryCostGs: 0,
      aiResponsesEnabled: true,
      bankName: "Banco E2E",
      bankBeneficiaryName: "Comercio de prueba",
      bankAccountNumber: `CTA-${suffix}`,
      bankDocumentNumber: "80000000-0",
      bankAlias: `e2e.${suffix.slice(0, 10)}`,
    },
  });
  expect(settings.ok(), "store setup should complete").toBeTruthy();

  const tenantResponse = await page.request.get(`${API_BASE_URL}/api/tenants/me`, { headers: authorization });
  expect(tenantResponse.ok()).toBeTruthy();
  const tenant = (await tenantResponse.json()) as { slug: string; storeSetupCompleted: boolean };
  expect(tenant.storeSetupCompleted).toBeTruthy();

  const productResponse = await page.request.post(`${API_BASE_URL}/api/products`, {
    headers: authorization,
    data: { name: productName, sku: `E2E-${suffix}`, price: 189000, discountPercent: 0, availableSizes: [], gender: "Unisex" },
  });
  expect(productResponse.ok(), `test product should be created (${productResponse.status()}): ${await productResponse.text()}`).toBeTruthy();
  const product = (await productResponse.json()) as { id: string };

  return {
    tenantName,
    email,
    password: E2E_PASSWORD,
    accessToken: auth.accessToken,
    slug: tenant.slug,
    accountExternalId,
    productId: product.id,
    productName,
    suffix,
  };
}

export async function createE2EConversation(page: Page, store: E2EStore, text: string) {
  const customerExternalId = `e2e-${store.suffix.slice(0, 12)}`;
  const inbound = await page.request.post(`${API_BASE_URL}/api/webhooks/whatsapp/mock`, {
    headers: { "X-Webhook-Signature": "mock-dev" },
    data: {
      eventId: `e2e-event-${store.suffix}`,
      accountExternalId: store.accountExternalId,
      customerExternalId,
      messageId: `e2e-message-${store.suffix}`,
      text,
    },
  });
  expect(inbound.ok(), `mock inbound message should be accepted (${inbound.status()}): ${await inbound.text()}`).toBeTruthy();
  const body = (await inbound.json()) as { conversationId: string };
  return { customerExternalId, conversationId: body.conversationId };
}

export async function signInE2EMerchant(page: Page, store: E2EStore) {
  await page.goto("/login");
  await page.getByLabel("Email").fill(store.email);
  await page.getByLabel("Contraseña").fill(store.password);
  await page.getByRole("button", { name: "Ingresar" }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
}