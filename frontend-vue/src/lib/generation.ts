import type { Translate } from "./format";

type SkippedGrave = string | { Number: string; Reason?: string };

interface GenerationResult {
  InvoicedGraves?: number;
  SkippedGraves?: SkippedGrave[];
}

/** "Erstellt: 3 (Übersprungen: 12 (keine Empfänger), 14)" -- port of the
 *  result message in Graves.controller.ts. */
export function generationSummary(resultJson: string | null | undefined, t: Translate): string {
  const result = JSON.parse(resultJson ?? "{}") as GenerationResult;
  const skipped = (result.SkippedGraves ?? [])
    .map((entry) => {
      const number = typeof entry === "string" ? entry : entry.Number;
      const reason =
        typeof entry === "object" && entry.Reason ? t(`skipReason${entry.Reason}`) : "";
      return reason ? `${number} (${reason})` : number;
    })
    .join(", ");
  return (
    `${t("generationDone")}: ${result.InvoicedGraves ?? 0}` +
    (skipped ? ` (${t("skippedGraves")}: ${skipped})` : "")
  );
}
