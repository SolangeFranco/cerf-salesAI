import { test, expect } from "@playwright/test";
import { createE2EStore } from "./fixtures";

test("shopper browses an isolated public store, checks out, and reaches payment receipt", async ({ page }) => {
  const store = await createE2EStore(page, "Producto Checkout E2E");
  await page.goto(`/store/${store.slug}`);
  await expect(page.getByRole("heading", { name: /Encontrá algo que/ })).toBeVisible();

  const productCard = page.locator("article").filter({ hasText: store.productName });
  await expect(productCard).toBeVisible();
  await productCard.getByRole("button", { name: "Agregar" }).click();
  await expect(productCard.getByRole("button", { name: "Agregado" })).toBeVisible();

  await page.getByRole("link", { name: /Carrito/ }).click();
  await expect(page).toHaveURL(new RegExp(`/store/${store.slug}/cart$`));
  await expect(page.getByText(store.productName)).toBeVisible();

  await page.getByPlaceholder("Nombre completo").fill("Cliente E2E Checkout");
  await page.getByPlaceholder("Teléfono").fill("0981234567");
  await page.getByPlaceholder("Dirección de entrega").fill("Av. Mariscal López 1234");
  await page.getByRole("button", { name: "Continuar al pago" }).click();

  await expect(page).toHaveURL(/\/checkout\//, { timeout: 10_000 });
  await expect(page.getByText(store.tenantName, { exact: true })).toBeVisible();
  await expect(page.getByText(store.productName).first()).toBeVisible();
});
