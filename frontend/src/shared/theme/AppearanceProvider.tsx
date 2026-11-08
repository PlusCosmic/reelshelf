import type { ReactNode } from "react";
import { AppearanceProvider as UiAppearanceProvider } from "@pluscosmic/ui";
import { appearanceStorageKey } from "./storageKey";

export { type Appearance, useAppearance } from "@pluscosmic/ui";

/** @pluscosmic/ui's provider under Reelshelf's local storage keys. */
export function AppearanceProvider({ children }: { children: ReactNode }) {
  return (
    <UiAppearanceProvider storageKey={appearanceStorageKey}>
      {children}
    </UiAppearanceProvider>
  );
}
