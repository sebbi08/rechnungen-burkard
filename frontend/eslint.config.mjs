import { defineConfigWithVueTs, vueTsConfigs } from "@vue/eslint-config-typescript";
import pluginVue from "eslint-plugin-vue";
import globals from "globals";

// "essential" rather than "recommended": the recommended set is mostly
// formatting, which Prettier owns here as everywhere else in the repository.
export default defineConfigWithVueTs(
  { ignores: ["dist/", "src/api/generated/", "scripts/"] },
  pluginVue.configs["flat/essential"],
  vueTsConfigs.recommendedTypeChecked,
  { languageOptions: { globals: globals.browser } },
);
