import { ref } from "vue";

const STORAGE_KEY = "invoicing-vue-theme";
const dark = ref(false);

function apply(isDark: boolean): void {
  dark.value = isDark;
  document.documentElement.classList.toggle("app-dark", isDark);
}

function stored(): "light" | "dark" | null {
  try {
    const value = localStorage.getItem(STORAGE_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

/** Before mount: the remembered choice, else the system's. */
export function applyStoredTheme(): void {
  const choice = stored();
  apply(choice ? choice === "dark" : window.matchMedia("(prefers-color-scheme: dark)").matches);
}

export function useTheme() {
  function toggle(): void {
    apply(!dark.value);
    try {
      localStorage.setItem(STORAGE_KEY, dark.value ? "dark" : "light");
    } catch {
      // storage unavailable (private mode): the choice lasts for this page only
    }
  }
  return { dark, toggle };
}
