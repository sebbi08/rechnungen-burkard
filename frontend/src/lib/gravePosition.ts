/** The backend's GravePosition members, in the order the editor offers them. */
export const GRAVE_POSITIONS = [
  "DoubleInside",
  "DoubleOutside",
  "SingleInside",
  "SingleOutside",
  "UrnInside",
  "UrnOutside",
] as const;

export type GravePositionName = (typeof GRAVE_POSITIONS)[number];
