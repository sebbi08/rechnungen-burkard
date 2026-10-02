import type { GraveBillingProblem } from "@/api/endpoints";
import { formatAmount, formatVatRate, type Translate } from "./format";

/** Why a grave's prices do not split evenly across its recipients, per VAT
 *  rate, with the nearest amounts that would work. */
export function billingProblemText(
  problems: GraveBillingProblem[],
  recipients: number,
  t: Translate,
): string {
  const lines = problems.map((problem) => {
    const sentence = t(recipients === 1 ? "pricesDoNotDivideLineOne" : "pricesDoNotDivideLine", [
      formatVatRate(problem.VatRate),
      formatAmount(problem.Amount),
      recipients,
    ]);
    const options = problem.Suggestions.map((value) => formatAmount(value));
    if (options.length >= 2) {
      return `${sentence} ${t("pricesNearbyValid", [options[0], options[1]])}`;
    }
    if (options.length === 1) {
      return `${sentence} ${t("pricesNearbyValidOne", [options[0]])}`;
    }
    return `${sentence} ${t("pricesAdjustAPrice")}`;
  });
  return [t("pricesDoNotDivideIntro"), "", ...lines].join("\n");
}
