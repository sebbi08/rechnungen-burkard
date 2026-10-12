import type { GravePosition } from "@/api/generated";

/** The backend's GravePosition members a grave can be given in the editor, in
 *  the order it offers them. The others come from the old program's data
 *  through the import; a grave keeps one of those but none is chosen anew. */
export const OFFERED_GRAVE_POSITIONS = [
  "DoubleInside",
  "DoubleOutside",
  "SingleInside",
  "SingleOutside",
  "UrnInside",
  "UrnOutside",
] as const satisfies readonly `${GravePosition}`[];

/** Any of the backend's GravePosition members, by name. */
export type GravePositionName = `${GravePosition}`;
