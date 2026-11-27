import { expect, test } from "@playwright/test";
import { login, watchCsp } from "./helpers";

// Image PNG de 2 × 2 pixels.
const png = Buffer.from(
  "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEUlEQVR4nGP4z8DA8B+MgBgAHfAD/dPQfSYAAAAASUVORK5CYII=",
  "base64",
);

test.describe.serial("Pièces jointes et API publique", () => {
  test("ticket joint à une opération, affiché puis signalé dans la liste", async ({
    page,
  }, testInfo) => {
    test.skip(
      testInfo.project.name !== "desktop",
      "Données créées une seule fois.",
    );
    const csp = watchCsp(page);
    await login(page);
    await page
      .locator(".v-navigation-drawer")
      .getByText("Opérations", { exact: true })
      .click();
    const item = page.locator(".v-list-item", { hasText: "Marché hors ligne" });
    await item.click();

    const attachments = page.getByRole("dialog").getByTestId("attachments");
    await expect(attachments).toContainText("Aucune");
    await attachments
      .getByTestId("attachment-input")
      .setInputFiles({
        name: "ticket.png",
        mimeType: "image/png",
        buffer: png,
      });
    await expect(attachments).toContainText("ticket.png");
    const thumbnail = attachments.locator("img");
    await expect(thumbnail).toBeVisible();
    await expect
      .poll(() =>
        thumbnail.evaluate((img: HTMLImageElement) => img.naturalWidth),
      )
      .toBe(2);
    const content = await page.request.get(
      (await thumbnail.getAttribute("src"))!,
    );
    expect(content.headers()["content-type"]).toBe("image/png");
    expect(content.headers()["content-security-policy"]).toContain("sandbox");

    // Un fichier qui n'est pas une image ni un PDF est refusé, quel que soit son nom.
    await attachments.getByTestId("attachment-input").setInputFiles({
      name: "facture.pdf",
      mimeType: "application/pdf",
      buffer: Buffer.from("<html>pas un PDF</html>"),
    });
    await expect(attachments).toContainText("Type de fichier non accepté");

    await page.getByRole("button", { name: "Annuler" }).click();
    await expect(item.getByLabel("1 pièce jointe")).toBeVisible();
    expect(csp).toEqual([]);
  });

  test("jeton d’accès en lecture : utilisé depuis un script, puis révoqué", async ({
    page,
    playwright,
  }, testInfo) => {
    test.skip(
      testInfo.project.name !== "desktop",
      "Données créées une seule fois.",
    );
    await login(page);
    await page
      .locator(".v-navigation-drawer")
      .getByText("Accès API", { exact: true })
      .click();
    await expect(
      page.getByRole("heading", { name: "Accès API" }),
    ).toBeVisible();

    const tokens = page.getByTestId("tokens");
    await tokens.getByLabel("Nom du jeton").fill("Tableur e2e");
    await tokens.getByRole("button", { name: "Créer" }).click();
    const token = (await page.getByTestId("new-token").textContent())!.trim();
    expect(token).toMatch(/^hb_/);
    await expect(tokens).toContainText("Tableur e2e");
    await expect(tokens).toContainText("Lecture seule");

    // Script : sans cookie ni jeton anti-CSRF, seulement l'en-tête Authorization.
    const script = await playwright.request.newContext({
      baseURL: testInfo.project.use.baseURL,
      extraHTTPHeaders: { Authorization: `Bearer ${token}` },
    });
    const accounts = await script.get("/api/accounts");
    expect(accounts.status()).toBe(200);
    expect((await accounts.json()).items.length).toBeGreaterThan(0);
    expect(
      (
        await script.post("/api/accounts", { data: { name: "Pirate" } })
      ).status(),
    ).toBe(403);
    expect((await script.get("/api/users")).status()).toBe(403);

    await tokens.getByRole("button", { name: "Révoquer" }).click();
    await tokens.getByRole("button", { name: "Confirmer" }).click();
    await expect(tokens).toContainText("Aucun jeton.");
    expect((await script.get("/api/accounts")).status()).toBe(401);
    await script.dispose();
  });

  test("webhook vers la domotique : secret montré une seule fois", async ({
    page,
  }, testInfo) => {
    test.skip(
      testInfo.project.name !== "desktop",
      "Données créées une seule fois.",
    );
    await login(page);
    await page
      .locator(".v-navigation-drawer")
      .getByText("Accès API", { exact: true })
      .click();
    const webhooks = page.getByTestId("webhooks");
    await webhooks.getByRole("button", { name: "Ajouter" }).click();

    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Nom").fill("Home Assistant");
    await dialog
      .getByLabel("Adresse")
      .fill("http://127.0.0.1:8123/api/webhook/budget");
    await dialog.getByRole("button", { name: "Enregistrer" }).click();
    // Boucle locale : refusée par le serveur.
    await expect(dialog).toContainText("Adresse refusée");

    await dialog
      .getByLabel("Adresse")
      .fill("http://192.168.1.20:8123/api/webhook/budget");
    await dialog.getByRole("button", { name: "Enregistrer" }).click();
    await expect(page.getByTestId("webhook-secret")).toHaveText(/^whsec_/);
    await expect(webhooks).toContainText("Home Assistant");
    await expect(webhooks).toContainText("Actif");
  });

  test("accès API depuis le mobile", async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== "mobile", "Navigation mobile.");
    await login(page);
    await page.getByRole("button", { name: "Compte" }).click();
    await page.getByRole("link", { name: "Accès API" }).click();
    await expect(
      page.getByRole("heading", { name: "Accès API" }),
    ).toBeVisible();
    await expect(page.getByTestId("webhooks")).toContainText("Home Assistant");
  });
});
