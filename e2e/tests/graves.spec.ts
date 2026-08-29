import { expect, test } from "@playwright/test";

// Unique per test run. `npm test` starts with an empty database, but
// `npm run test:dev` runs against the dev stack, where fixed numbers would
// collide with pre-existing rows or make assertions vacuous.
const RUN = Date.now();

async function seedGraveGenerationData(
  page: import("@playwright/test").Page,
  count: number,
): Promise<string> {
  // VatRate 7 may already exist from previous runs; a 409 here is harmless.
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: {
        Number: `8001-${RUN}`,
        Description: "Grabpflege",
        VatRate: 7,
        DefaultPrice: 100,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: `Anna Beispiel ${RUN}`, Address: "Hauptstraße 1" },
    })
  ).json();

  // self-heal leftovers from interrupted runs: graves without item/recipient
  // links would be skipped by the generator and keep the duplicate check
  // (409 "bereits") from firing. Only the gaps are filled, so existing
  // grave totals are not inflated.
  const itemLinks = await (
    await page.request.get(
      "/odata/GraveItems?$select=GraveId",
    )
  ).json();
  const recipientLinks = await (
    await page.request.get(
      "/odata/GraveRecipients?$select=GraveId",
    )
  ).json();
  const withItems = new Set<number>(
    (itemLinks.value ?? []).map((link: any) => link.GraveId),
  );
  const withRecipients = new Set<number>(
    (recipientLinks.value ?? []).map((link: any) => link.GraveId),
  );
  const existing = await (
    await page.request.get("/odata/Graves?$select=Id")
  ).json();
  for (const grave of existing.value ?? []) {
    if (!withItems.has(grave.Id)) {
      await page.request.post("/odata/GraveItems", {
        data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
      });
    }
    if (!withRecipients.has(grave.Id)) {
      await page.request.post("/odata/GraveRecipients", {
        data: { GraveId: grave.Id, RecipientId: recipient.Id },
      });
    }
  }

  const firstGraveNumber = `G-${RUN}-001`;
  for (let i = 1; i <= count; i++) {
    const grave = await (
      await page.request.post("/odata/Graves", {
        data: {
          GraveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
          StartDate: "2026-01-01",
          EndDate: "2040-12-31",
        },
      })
    ).json();
    await page.request.post("/odata/GraveItems", {
      data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
    });
    await page.request.post("/odata/GraveRecipients", {
      data: { GraveId: grave.Id, RecipientId: recipient.Id },
    });
  }
  return firstGraveNumber;
}

test("half-year generation blocks the UI and prevents duplicates (Stories 6, 13)", async ({
  page,
}) => {
  const firstGraveNumber = await seedGraveGenerationData(page, 60);
  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  await expect(gravesTable).toContainText(firstGraveNumber);

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();

  // blocking progress indicator is visible while the job runs
  await expect(page.locator(".sapMBusyDialog")).toBeVisible({ timeout: 5000 });
  await expect(page.locator(".sapMBusyDialog")).not.toBeVisible({
    timeout: 90000,
  });

  // status column now shows graves as invoiced
  await expect(gravesTable.getByText("erstellt").first()).toBeVisible();

  // dismiss the success message, then run the second generation
  await page.locator(".sapMDialog").getByRole("button", { name: "OK" }).click();
  expect(await periodInvoices(page)).not.toHaveLength(0);

  await page.getByRole("button", { name: "Rechnungen erzeugen" }).click();
  await page.getByRole("button", { name: "Jetzt erzeugen" }).click();
  // the busy dialog only appears when a job actually runs (409 case: not at all)
  await page
    .locator(".sapMBusyDialog")
    .waitFor({ state: "visible", timeout: 3000 })
    .catch(() => {});
  await page
    .locator(".sapMBusyDialog")
    .waitFor({ state: "hidden", timeout: 90000 })
    .catch(() => {});
  // either the German "already invoiced" message (when every grave is invoiced)
  // or the result of a second run
  const resultBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(resultBox).toBeVisible();
  const resultText = await resultBox.innerText();
  expect(
    resultText.includes("bereits") || resultText.includes("Erstellt"),
  ).toBeTruthy();
  await page.locator(".sapMDialog").getByRole("button").first().click();

  // FR-027: no grave and recipient may end up with two invoices for the same
  // half-year. Asserting the pairs rather than a total keeps this immune to the
  // graves other specs create in parallel, which a second run may legitimately
  // invoice for the first time.
  const pairs = await periodInvoices(page);
  const seen = pairs.map((i) => `${i.GraveId}:${i.RecipientId}`);
  expect(new Set(seen).size).toBe(seen.length);
});

interface PeriodInvoice {
  GraveId: number;
  RecipientId: number;
}

/** Every grave invoice of the 2026 first half-year, paged past the service's
 *  200-row limit so the duplicate check sees all of them. */
async function periodInvoices(
  page: import("@playwright/test").Page,
): Promise<PeriodInvoice[]> {
  const pageSize = 200;
  const all: PeriodInvoice[] = [];
  for (let skip = 0; ; skip += pageSize) {
    const response = await page.request.get(
      "/odata/Invoices?$filter=PeriodYear eq 2026 and PeriodHalf eq 1" +
        `&$select=GraveId,RecipientId&$orderby=Id&$top=${pageSize}&$skip=${skip}`,
    );
    const batch = (await response.json()).value as PeriodInvoice[];
    all.push(...batch);
    if (batch.length < pageSize) {
      return all;
    }
  }
}

test("create a grave with item, price and recipient in one save (Story 5)", async ({
  page,
}) => {
  const itemNumber = `8002-${RUN}`;
  const recipientName = `Bert Beispiel ${RUN}`;
  const graveNumber = `X-${RUN}`;

  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("/odata/InvoiceItems", {
    data: {
      Number: itemNumber,
      Description: "Pflege",
      VatRate: 7,
      DefaultPrice: 50,
    },
  });
  await page.request.post("/odata/Recipients", {
    data: { Name: recipientName, Address: "Nebenweg 2" },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  // scope to the dialog: the toolbar search field's accessible name also
  // contains "Grabnummer" and would win the race while the dialog loads
  const graveDialog = page.locator(".sapMDialog");
  await graveDialog.getByLabel("Grabnummer").fill(graveNumber);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");

  // the grave does not exist yet: item and recipient are collected in the dialog
  await page.getByRole("button", { name: "Zeile hinzufügen" }).click();
  const itemInput = page.locator(
    ".sapMDialog input[id*='graveItem-'][id$='-inner']",
  );
  await itemInput.click();
  await itemInput.fill(itemNumber);
  await page.locator("div.sapMPopover li", { hasText: itemNumber }).click();
  await graveDialog.getByLabel("Anzahl").fill("2");
  await graveDialog.getByLabel("Preis").fill("55");

  await page.locator(".sapMDialog .sapMSlt[id$='linkRecipient']").click();
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: recipientName,
    })
    .click();
  // "Hinzufügen" alone also matches the item table's "Zeile hinzufügen"
  await page.getByRole("button", { name: "Hinzufügen", exact: true }).click();
  await expect(
    page.locator("[id$='graveRecipientsTable-listUl']"),
  ).toContainText(recipientName);

  // nothing has been persisted so far — one save writes the grave and both links
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(gravesTable).toContainText(graveNumber);

  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`,
    )
  ).json();
  const graveId = graves.value[0].Id;
  const links = await (
    await page.request.get(
      `/odata/GraveItems?$filter=GraveId eq ${graveId}`,
    )
  ).json();
  expect(links.value).toHaveLength(1);
  expect(Number(links.value[0].Count)).toBe(2);
  expect(Number(links.value[0].UnitPrice)).toBe(55);
  const recipientLinks = await (
    await page.request.get(
      `/odata/GraveRecipients?$filter=GraveId eq ${graveId}`,
    )
  ).json();
  expect(recipientLinks.value).toHaveLength(1);

  // reopening the grave shows the saved count and price again
  await gravesTable
    .locator("tbody tr", { hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await expect(graveDialog.getByLabel("Anzahl")).toHaveValue("2");
  await expect(graveDialog.getByLabel("Preis")).toHaveValue("55");
  // the dialog is built once by loadFragment and reused: the first open above
  // was in create mode (Id === null, warnings hidden, recipient controls
  // shown), and this is the same fragment instance reopened in edit mode
  // (Id !== null) with a fresh graveDraft model. Asserting the edit-mode
  // state here — not just in a fresh-page test — proves the visibility
  // bindings re-evaluate when the draft model is replaced, not merely on
  // first render.
  await expect(graveDialog).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(graveDialog).toContainText("Empfänger eines Grabs");
  // scoped to the recipients section: the Items panel has its own always-visible
  // "Zeile hinzufügen" (contains "Hinzufügen" as a substring) and "Löschen" controls
  const recipientsTable = graveDialog.locator("[id$='graveRecipientsTable']");
  const recipientsLink = graveDialog.locator("[id$='graveRecipientsLink']");
  await expect(recipientsLink.getByRole("button", { name: "Hinzufügen" })).toBeHidden();
  await expect(recipientsTable.getByRole("button", { name: "Löschen" })).toBeHidden();
  await page.getByRole("button", { name: "Abbrechen" }).click();
});

test("an item without a default price needs an explicit price (Story 6)", async ({
  page,
}) => {
  const noPriceItemNumber = `NP-${RUN}`;
  const graveNumber = `NPG-${RUN}`;
  await page.request.post("/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("/odata/InvoiceItems", {
    data: {
      Number: noPriceItemNumber,
      Description: "Ohne Preis",
      VatRate: 7,
      DefaultPrice: null,
    },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const graveDialog = page.locator(".sapMDialog");

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  await graveDialog.getByLabel("Grabnummer").fill(graveNumber);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");
  await page.getByRole("button", { name: "Zeile hinzufügen" }).click();
  const itemInput = page.locator(
    ".sapMDialog input[id*='graveItem-'][id$='-inner']",
  );
  await itemInput.click();
  await itemInput.fill(noPriceItemNumber);
  await page
    .locator("div.sapMPopover li", { hasText: noPriceItemNumber })
    .click();

  // no price anywhere: saving is rejected with the German "missing price" message
  await page.getByRole("button", { name: "Speichern" }).click();
  const errorBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(errorBox).toContainText("fehlt ein Preis");
  await errorBox.getByRole("button").first().click();
  await expect(gravesTable).not.toContainText(graveNumber);

  // entering the price in the dialog makes the same grave saveable
  await graveDialog.getByLabel("Preis").fill("40");
  await page.getByRole("button", { name: "Speichern" }).click();
  await expect(gravesTable).toContainText(graveNumber);

  const graves = await (
    await page.request.get(
      `/odata/Graves?$filter=GraveNumber eq '${graveNumber}'&$select=Id`,
    )
  ).json();
  const links = await (
    await page.request.get(
      `/odata/GraveItems?$filter=GraveId eq ${graves.value[0].Id}`,
    )
  ).json();
  expect(Number(links.value[0].UnitPrice)).toBe(40);
});

test("generation result names the skip reason for an unpriced item (Story 6)", async ({
  page,
}) => {
  const graveNumber = `SK-${RUN}`;
  const noPriceItem = await (
    await page.request.post("/odata/InvoiceItems", {
      data: {
        Number: `SP-${RUN}`,
        Description: "Ohne Preis",
        VatRate: 7,
        DefaultPrice: null,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: `Skip Beispiel ${RUN}`, Address: "Kiesweg 3" },
    })
  ).json();
  const grave = await (
    await page.request.post("/odata/Graves", {
      data: {
        GraveNumber: graveNumber,
        StartDate: "2026-01-01",
        EndDate: "2040-12-31",
      },
    })
  ).json();
  await page.request.post("/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: noPriceItem.Id, Count: 1 },
  });
  await page.request.post("/odata/GraveRecipients", {
    data: { GraveId: grave.Id, RecipientId: recipient.Id },
  });

  // 2030 H2 is not used by the other tests, so no duplicate conflicts
  const start = await page.request.post(
    "/odata/GenerateGraveInvoices",
    { data: { year: 2030, half: 2 } },
  );
  const jobRef = await start.json();
  let job: any;
  for (let i = 0; i < 80; i++) {
    job = await (
      await page.request.get(
        `/odata/Jobs(${jobRef.Id})`,
      )
    ).json();
    if (job.Status === "Succeeded" || job.Status === "Failed") {
      break;
    }
    await page.waitForTimeout(250);
  }
  expect(job.Status).toBe("Succeeded");
  expect(job.ResultJson).toContain(graveNumber);
  expect(job.ResultJson).toContain("ItemWithoutPrice");
});

test("editing a grave warns about future invoices and freezes its recipients", async ({
  page,
}) => {
  const marker = Date.now();
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: { Number: `7100-${marker}`, Description: "Grabpflege", VatRate: 7, DefaultPrice: 100 },
    })
  ).json();
  const recipient = await (
    await page.request.post("/odata/Recipients", {
      data: { Name: `Fest ${marker}`, Address: "Festweg 1" },
    })
  ).json();
  const grave = await (
    await page.request.post("/odata/Graves", {
      data: { GraveNumber: `F-${marker}`, StartDate: "2026-01-01", EndDate: "2040-12-31" },
    })
  ).json();
  await page.request.post("/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
  });
  await page.request.post("/odata/GraveRecipients", {
    data: { GraveId: grave.Id, RecipientId: recipient.Id },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  const row = gravesTable.locator("tbody tr", { hasText: `F-${marker}` });
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: "Bearbeiten" }).click();

  const dialog = page.locator("[id$='graveDialog']");
  await expect(dialog).toContainText("nur auf künftig erstellte Rechnungen");
  await expect(dialog).toContainText("Empfänger eines Grabs");
  // the recipient stays visible but cannot be removed or added to; the item
  // table has its own "Löschen"/"Hinzufügen" controls (always visible), so
  // these are scoped to the recipients section specifically
  await expect(dialog.getByText(`Fest ${marker}`)).toBeVisible();
  const recipientsTable = dialog.locator("[id$='graveRecipientsTable']");
  const recipientsLink = dialog.locator("[id$='graveRecipientsLink']");
  await expect(recipientsLink.getByRole("button", { name: "Hinzufügen" })).toBeHidden();
  await expect(recipientsTable.getByRole("button", { name: "Löschen" })).toBeHidden();
});

/**
 * Generates the grave invoices of one half-year and waits for the job. Each
 * caller uses a period no other test touches, so a run never collides with the
 * generation tests above or with another test's expectations.
 */
async function generateFor(
  page: import("@playwright/test").Page,
  year: number,
  half: number,
): Promise<void> {
  const response = await page.request.post("/odata/GenerateGraveInvoices", {
    data: { year, half },
  });
  expect(response.status()).toBe(202);
  const job = await response.json();
  await expect
    .poll(
      async () =>
        (await (await page.request.get(`/odata/Jobs(${job.Id})`)).json()).Status,
      { timeout: 90000 },
    )
    .toBe("Succeeded");
}

/** A grave with one item at 100.00 and the given recipients, unique per run. */
async function seedGrave(
  page: import("@playwright/test").Page,
  marker: string,
  recipientNames: string[],
): Promise<{ graveId: number; recipientNames: string[] }> {
  await page.request.post("/odata/VatRates", { data: { Rate: 7 } });
  const item = await (
    await page.request.post("/odata/InvoiceItems", {
      data: {
        Number: `${marker}-item`,
        Description: "Grabpflege",
        VatRate: 7,
        DefaultPrice: 100,
      },
    })
  ).json();
  const grave = await (
    await page.request.post("/odata/Graves", {
      data: {
        GraveNumber: marker,
        StartDate: "2026-01-01",
        EndDate: "2040-12-31",
      },
    })
  ).json();
  await page.request.post("/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
  });
  for (const name of recipientNames) {
    const recipient = await (
      await page.request.post("/odata/Recipients", {
        data: { Name: name, Address: "Musterweg 1" },
      })
    ).json();
    await page.request.post("/odata/GraveRecipients", {
      data: { GraveId: grave.Id, RecipientId: recipient.Id },
    });
  }
  return { graveId: grave.Id, recipientNames };
}

test("the grave detail page shows the grave, its status and its invoices", async ({
  page,
}) => {
  const marker = `GD-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  await page.goto(`/index.html#/graves/${graveId}`);

  await expect(page.getByText(marker, { exact: true })).toBeVisible();
  // this grave was created after the generation tests above ran, so nothing has
  // invoiced it for the current half-year
  await expect(page.getByText("offen").first()).toBeVisible();
  for (const name of names) {
    await expect(page.getByText(name)).toBeVisible();
  }
  await expect(page.getByText("Für dieses Grab wurden noch keine")).toBeVisible();

  // a period no other test uses, so the rows below are exactly this generation
  await generateFor(page, 2039, 2);
  await page.reload();

  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable).toContainText("H2 2039");
  // each recipient pays half of 1 × 100.00 + 7% = 53,50
  await expect(invoicesTable).toContainText("53,50 €");

  // both documents are reachable per row
  const row = invoicesTable.locator("tbody tr", { hasText: names[0] });
  await expect(row.getByRole("button", { name: "Rechnung herunterladen" })).toBeVisible();
  await expect(row.getByRole("button", { name: "Kopie herunterladen" })).toBeVisible();
});

test("editing a grave invoice revises every recipient", async ({ page }) => {
  const marker = `GE-${Date.now()}`;
  const names = [`Anna ${marker}`, `Bernd ${marker}`];
  const { graveId } = await seedGrave(page, marker, names);

  // its own period, so the rows below belong to this test alone
  await generateFor(page, 2038, 2);

  await page.goto(`/index.html#/graves/${graveId}`);
  const invoicesTable = page.locator("[id$='graveInvoicesTable-listUl']");
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);

  await invoicesTable
    .locator("tbody tr", { hasText: names[0] })
    .getByRole("button", { name: "Bearbeiten" })
    .click();

  // the dialog names the half-year and both recipients it is about to revise
  const dialog = page.locator("[id$='graveInvoiceDialog']");
  await expect(dialog).toContainText("H2 2038");
  await expect(dialog).toContainText(names[0]);
  await expect(dialog).toContainText(names[1]);
  // it opens on the grave's price, not on the 50.00 share each recipient pays
  await expect(dialog.getByLabel("Preis")).toHaveValue("100");

  await dialog.getByLabel("Preis").fill("200");
  await dialog.getByRole("button", { name: "Speichern" }).click();

  // both recipients moved to R1, each at half of 1 × 200.00 + 7% = 107,00 —
  // asserted per row, not just "at least one", so a revision that reached
  // only one recipient would fail this
  await expect(invoicesTable.locator("tbody tr")).toHaveCount(2);
  await expect(invoicesTable.locator("tbody tr", { hasText: "R1" })).toHaveCount(2);
  await expect(
    invoicesTable.locator("tbody tr", { hasText: "107,00 €" }),
  ).toHaveCount(2);
});
