import { test, expect } from "@playwright/test";
import { createE2EConversation, createE2EStore, signInE2EMerchant } from "./fixtures";

test("merchant can sign in and reach core surfaces with an empty tenant dashboard", async ({ page }) => {
  const store = await createE2EStore(page, "Producto Navegación E2E");
  const conversation = await createE2EConversation(page, store, "Hola, quiero consultar por un producto.");

  await signInE2EMerchant(page, store);
  await expect(page.getByRole("heading", { name: "Resumen de tu tienda" })).toBeVisible();
  await expect(page.getByText("Sin datos", { exact: true })).toBeVisible();

  await page.goto("/dashboard/products");
  await expect(page.getByRole("heading", { name: "Lista de productos" })).toBeVisible();
  await expect(page.getByText(store.productName)).toBeVisible();

  await page.goto("/dashboard/customers");
  await expect(page.getByRole("heading", { name: "Clientes" })).toBeVisible();
  await expect(page.getByText(conversation.customerExternalId).first()).toBeVisible();

  await page.goto("/dashboard/conversations");
  await expect(page.getByRole("heading", { name: "Conversaciones" })).toBeVisible();
  await expect(page.getByRole("link", { name: conversation.customerExternalId })).toBeVisible();
});

test("merchant can request an AI response based on its own catalog", async ({ page }) => {
  const store = await createE2EStore(page, "Camisa Agente E2E");
  const conversation = await createE2EConversation(page, store, store.productName);

  await signInE2EMerchant(page, store);
  await page.goto("/dashboard/conversations");
  await page.getByRole("link", { name: conversation.customerExternalId }).click();
  await expect(page.getByText(store.productName).first()).toBeVisible();
  await page.getByRole("button", { name: "Responder con agente" }).click();
  await expect(page.getByText(new RegExp(`Encontré .*${store.productName}`)).first()).toBeVisible();
});

test("merchant can pause the AI, take control, and resume it", async ({ page }) => {
  const store = await createE2EStore(page, "Producto Handoff E2E");
  const conversation = await createE2EConversation(page, store, "Necesito ayuda de una persona.");

  await signInE2EMerchant(page, store);
  await page.goto("/dashboard/conversations");
  await page.getByRole("link", { name: conversation.customerExternalId }).click();

  const conversationHeader = page.locator("section header");
  await expect(conversationHeader.getByText("IA activa", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Pausar IA" }).click();
  await expect(conversationHeader.getByText("Pausado", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Tomar control" }).click();
  await expect(conversationHeader.getByText("Atendido por vos", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Activar IA" }).click();
  await expect(conversationHeader.getByText("IA activa", { exact: true })).toBeVisible();
});


