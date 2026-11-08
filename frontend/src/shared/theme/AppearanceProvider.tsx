import {
  type ReactNode,
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import {
  type ColorScheme,
  type ReelshelfTheme,
  type ThemePreference,
  applyColorScheme,
  darkSchemeQuery,
  getStoredColorScheme,
  getStoredTheme,
  getSystemTheme,
  storeColorScheme,
  storeTheme,
} from "./appearance";

interface Appearance {
  /** The theme in use: the stored pick, else the OS setting. */
  theme: ReelshelfTheme;
  /** What the owner picked, or "system" to follow the OS. */
  themePreference: ThemePreference;
  setThemePreference: (preference: ThemePreference) => void;
  toggleTheme: () => void;
  colorScheme: ColorScheme;
  /** Applies and remembers a scheme on this browser; saving it to the account is the caller's job. */
  setColorScheme: (scheme: ColorScheme) => void;
}

const AppearanceContext = createContext<Appearance | null>(null);

export function AppearanceProvider({ children }: { children: ReactNode }) {
  const [storedTheme, setStoredTheme] = useState<ReelshelfTheme | null>(
    getStoredTheme,
  );
  const [systemTheme, setSystemTheme] =
    useState<ReelshelfTheme>(getSystemTheme);
  const [colorScheme, setColorSchemeState] =
    useState<ColorScheme>(getStoredColorScheme);
  const theme = storedTheme ?? systemTheme;

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  useEffect(() => {
    applyColorScheme(colorScheme);
  }, [colorScheme]);

  useEffect(() => {
    const query = window.matchMedia(darkSchemeQuery);
    const followSystem = () => setSystemTheme(getSystemTheme());
    query.addEventListener("change", followSystem);
    return () => query.removeEventListener("change", followSystem);
  }, []);

  const setThemePreference = useCallback((preference: ThemePreference) => {
    const stored = preference === "system" ? null : preference;
    storeTheme(stored);
    setStoredTheme(stored);
  }, []);

  const setColorScheme = useCallback((scheme: ColorScheme) => {
    storeColorScheme(scheme);
    setColorSchemeState(scheme);
  }, []);

  const value = useMemo<Appearance>(
    () => ({
      theme,
      themePreference: storedTheme ?? "system",
      setThemePreference,
      toggleTheme: () =>
        setThemePreference(theme === "dark" ? "light" : "dark"),
      colorScheme,
      setColorScheme,
    }),
    [theme, storedTheme, setThemePreference, colorScheme, setColorScheme],
  );

  return (
    <AppearanceContext.Provider value={value}>
      {children}
    </AppearanceContext.Provider>
  );
}

export function useAppearance(): Appearance {
  const appearance = useContext(AppearanceContext);
  if (!appearance) {
    throw new Error("useAppearance needs an AppearanceProvider");
  }
  return appearance;
}
