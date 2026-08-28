import { expect, test } from "@playwright/test";

// Unique per test run: the dev DB persists between runs, so fixed numbers would
// either collide with pre-existing rows or make assertions vacuous.
const RUN = Date.now();

async function seedGraveGenerationData(
  page: import("@playwright/test").Page,
  count: number,
): Promise<string> {
  // VatRate 7 may already exist from previous runs; a 409 here is harmless.
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  const item = await (
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
      data: {
        Number: `8001-${RUN}`,
        Description: "Grabpflege",
        VatRate: 7,
        DefaultPrice: 100,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("http://localhost:8080/odata/Recipients", {
      data: { Name: `Anna Beispiel ${RUN}`, Address: "Hauptstraße 1" },
    })
  ).json();

  // self-heal leftovers from interrupted runs: graves without item/recipient
  // links would be skipped by the generator and keep the duplicate check
  // (409 "bereits") from firing. Only the gaps are filled, so existing
  // grave totals are not inflated.
  const itemLinks = await (
    await page.request.get(
      "http://localhost:8080/odata/GraveItems?$select=GraveId",
    )
  ).json();
  const recipientLinks = await (
    await page.request.get(
      "http://localhost:8080/odata/GraveRecipients?$select=GraveId",
    )
  ).json();
  const withItems = new Set<number>(
    (itemLinks.value ?? []).map((link: any) => link.GraveId),
  );
  const withRecipients = new Set<number>(
    (recipientLinks.value ?? []).map((link: any) => link.GraveId),
  );
  const existing = await (
    await page.request.get("http://localhost:8080/odata/Graves?$select=Id")
  ).json();
  for (const grave of existing.value ?? []) {
    if (!withItems.has(grave.Id)) {
      await page.request.post("http://localhost:8080/odata/GraveItems", {
        data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
      });
    }
    if (!withRecipients.has(grave.Id)) {
      await page.request.post("http://localhost:8080/odata/GraveRecipients", {
        data: { GraveId: grave.Id, RecipientId: recipient.Id },
      });
    }
  }

  const firstGraveNumber = `G-${RUN}-001`;
  for (let i = 1; i <= count; i++) {
    const grave = await (
      await page.request.post("http://localhost:8080/odata/Graves", {
        data: {
          GraveNumber: `G-${RUN}-${String(i).padStart(3, "0")}`,
          StartDate: "2026-01-01",
          EndDate: "2040-12-31",
        },
      })
    ).json();
    await page.request.post("http://localhost:8080/odata/GraveItems", {
      data: { GraveId: grave.Id, ItemId: item.Id, Count: 1 },
    });
    await page.request.post("http://localhost:8080/odata/GraveRecipients", {
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

  // a second generation must not create duplicate invoices: either it is
  // blocked with the German "already invoiced" message (when every grave is
  // invoiced) or it re-runs and invoices no grave again (leftover graves
  // without priced items stay skipped)
  const invoiceCount = async (): Promise<number> => {
    const response = await page.request.get(
      "http://localhost:8080/odata/Invoices/$count?$filter=PeriodYear eq 2026 and PeriodHalf eq 1",
    );
    return Number(await response.text());
  };

  // dismiss the success message, then run the second generation
  await page.locator(".sapMDialog").getByRole("button", { name: "OK" }).click();
  const countBefore = await invoiceCount();
  expect(countBefore).toBeGreaterThan(0);
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
  const resultBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(resultBox).toBeVisible();
  const resultText = await resultBox.innerText();
  expect(
    resultText.includes("bereits") || resultText.includes("Erstellt: 0"),
  ).toBeTruthy();
  await page.locator(".sapMDialog").getByRole("button").first().click();

  // and no invoice may have been added for the half-year
  expect(await invoiceCount()).toBe(countBefore);
});

test("create a grave and link item and recipient (Story 5)", async ({
  page,
}) => {
  const itemNumber = `8002-${RUN}`;
  const recipientName = `Bert Beispiel ${RUN}`;
  const graveNumber = `X-${RUN}`;

  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  await page.request.post("http://localhost:8080/odata/InvoiceItems", {
    data: {
      Number: itemNumber,
      Description: "Pflege",
      VatRate: 7,
      DefaultPrice: 50,
    },
  });
  await page.request.post("http://localhost:8080/odata/Recipients", {
    data: { Name: recipientName, Address: "Nebenweg 2" },
  });

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");

  await page.getByRole("button", { name: "Grab anlegen" }).click();
  // scope to the dialog: the new toolbar search field's accessible name also
  // contains "Grabnummer" and would win the race while the dialog loads
  const graveDialog = page.locator(".sapMDialog");
  await graveDialog.getByLabel("Grabnummer").fill(graveNumber);
  await graveDialog.getByLabel("Beginn").fill("2026-01-01");
  await graveDialog.getByLabel("Ende").fill("2040-12-31");
  await page.getByRole("button", { name: "Speichern" }).click();

  await expect(gravesTable).toContainText(graveNumber);

  // open the edit dialog for the newly created grave and add the item and
  // recipient links; ".first()" would edit an unrelated pre-existing row
  await gravesTable
    .locator("tbody tr", { hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await page.locator(".sapMDialog .sapMSlt[id$='linkItem']").click();
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: itemNumber,
    })
    .click();
  await page.locator("[id$='linkCount-inner']").fill("1");
  await page.getByRole("button", { name: "Hinzufügen" }).first().click();
  // scope to the dialog's own items table: the select keeps a hidden inline
  // copy of its options in the DOM, which would make a dialog-wide assertion vacuous
  await expect(page.locator("[id$='graveItemsTable-listUl']")).toContainText(
    itemNumber,
  );

  await page.locator(".sapMDialog .sapMSlt[id$='linkRecipient']").click();
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: recipientName,
    })
    .click();
  await page.getByRole("button", { name: "Hinzufügen" }).last().click();
  await expect(
    page.locator("[id$='graveRecipientsTable-listUl']"),
  ).toContainText(recipientName);

  await page.getByRole("button", { name: "Speichern" }).click();
});

test("linking an item without a default price is rejected (Story 6)", async ({
  page,
}) => {
  const noPriceItemNumber = `NP-${RUN}`;
  const graveNumber = `NPG-${RUN}`;
  await page.request.post("http://localhost:8080/odata/VatRates", {
    data: { Rate: 7 },
  });
  const noPriceItem = await (
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
      data: {
        Number: noPriceItemNumber,
        Description: "Ohne Preis",
        VatRate: 7,
        DefaultPrice: null,
      },
    })
  ).json();
  const grave = await (
    await page.request.post("http://localhost:8080/odata/Graves", {
      data: {
        GraveNumber: graveNumber,
        StartDate: "2026-01-01",
        EndDate: "2040-12-31",
      },
    })
  ).json();

  await page.goto("/index.html#/graves");
  const gravesTable = page.locator("[id$='gravesTable-listUl']");
  await expect(gravesTable).toContainText(graveNumber);

  await gravesTable
    .locator("tbody tr", { hasText: graveNumber })
    .getByRole("button", { name: "Bearbeiten" })
    .click();
  await page.locator(".sapMDialog .sapMSlt[id$='linkItem']").click();
  await page
    .locator("ul.sapMSltList-CTX li.sapMSelectListItem", {
      hasText: noPriceItemNumber,
    })
    .click();
  await page.locator("[id$='linkCount-inner']").fill("1");
  await page.getByRole("button", { name: "Hinzufügen" }).first().click();

  // the link is rejected with the German "missing price" message
  const errorBox = page.locator(".sapMDialog.sapMMessageBox");
  await expect(errorBox).toContainText("fehlt ein Preis");
  await errorBox.getByRole("button").first().click();
  await page.getByRole("button", { name: "Abbrechen" }).click();

  // no link may have been created for the rejected item
  const links = await (
    await page.request.get(
      `http://localhost:8080/odata/GraveItems?$filter=GraveId eq ${grave.Id}&$select=ItemId`,
    )
  ).json();
  const linkedIds = (links.value ?? []).map((link: any) => link.ItemId);
  expect(linkedIds).not.toContain(noPriceItem.Id);
});

test("generation result names the skip reason for an unpriced item (Story 6)", async ({
  page,
}) => {
  const graveNumber = `SK-${RUN}`;
  const noPriceItem = await (
    await page.request.post("http://localhost:8080/odata/InvoiceItems", {
      data: {
        Number: `SP-${RUN}`,
        Description: "Ohne Preis",
        VatRate: 7,
        DefaultPrice: null,
      },
    })
  ).json();
  const recipient = await (
    await page.request.post("http://localhost:8080/odata/Recipients", {
      data: { Name: `Skip Beispiel ${RUN}`, Address: "Kiesweg 3" },
    })
  ).json();
  const grave = await (
    await page.request.post("http://localhost:8080/odata/Graves", {
      data: {
        GraveNumber: graveNumber,
        StartDate: "2026-01-01",
        EndDate: "2040-12-31",
      },
    })
  ).json();
  await page.request.post("http://localhost:8080/odata/GraveItems", {
    data: { GraveId: grave.Id, ItemId: noPriceItem.Id, Count: 1 },
  });
  await page.request.post("http://localhost:8080/odata/GraveRecipients", {
    data: { GraveId: grave.Id, RecipientId: recipient.Id },
  });

  // 2030 H2 is not used by the other tests, so no duplicate conflicts
  const start = await page.request.post(
    "http://localhost:8080/odata/GenerateGraveInvoices",
    { data: { year: 2030, half: 2 } },
  );
  const jobRef = await start.json();
  let job: any;
  for (let i = 0; i < 80; i++) {
    job = await (
      await page.request.get(
        `http://localhost:8080/odata/Jobs(${jobRef.Id})`,
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
