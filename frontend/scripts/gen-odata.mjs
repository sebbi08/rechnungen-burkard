// Refreshes the OData client: downloads $metadata into src/api/metadata.xml
// and regenerates src/api/generated/ from it. Both are committed, so a build
// never needs a running backend -- run this only when the EDM changes.
//
// Defaults to the E2E stack. The metadata is the same on every stack, and the
// dev stack is the user's (CLAUDE.md).
import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";

const url = process.env.METADATA_URL ?? "http://localhost:8081/odata/$metadata";
const response = await fetch(url);
if (!response.ok) {
  throw new Error(`GET ${url} answered ${response.status}`);
}
writeFileSync("src/api/metadata.xml", await response.text());

execFileSync(
  "npx",
  [
    "odata2ts",
    "--source",
    "src/api/metadata.xml",
    "--output",
    "src/api/generated",
    "--mode",
    "service",
    "--emit-mode",
    "ts",
    "--service-name",
    "Invoicing",
  ],
  { stdio: "inherit" },
);
