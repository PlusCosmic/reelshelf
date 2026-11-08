import { useState } from "react";
import { settingsCopy } from "@/copy/settings";
import { useSetColorScheme } from "@/hooks/auth.queries";
import { ApiError } from "@/shared/services/apiError";
import { useAppearance } from "@/shared/theme/AppearanceProvider";
import {
  type ColorScheme,
  type ThemePreference,
  colorSchemes,
} from "@/shared/theme/appearance";

const copy = settingsCopy.appearance;
const themePreferences: ThemePreference[] = ["light", "dark", "system"];
// A few spines from the landing page's built-in shelf, the same in every preview, as game covers are.
const previewCloths = ["#b06c51", "#5a9148", "#2f2e57", "#928111", "#326295"];

/**
 * Light or dark (kept on this browser, as the profile menu's toggle is) and the colour scheme (kept on the
 * account). Each scheme's preview carries its own `data-color-scheme`, so it shows that scheme whatever is in use.
 */
export function AppearanceSettings() {
  const { theme, themePreference, setThemePreference, colorScheme } =
    useAppearance();
  const saveColorScheme = useSetColorScheme();
  const [error, setError] = useState<string | null>(null);

  const choose = (scheme: ColorScheme) => {
    if (scheme === colorScheme) return;
    setError(null);
    saveColorScheme.mutate(scheme, {
      onError: (failure) =>
        setError(
          failure instanceof ApiError
            ? failure.message
            : copy.colorScheme.saveFailed,
        ),
    });
  };

  return (
    <div className="rs-appearance">
      <div className="rs-appearance-field">
        <span className="rs-appearance-label" id="appearance-theme">
          {copy.theme.label}
        </span>
        <div
          className="rs-plan-interval"
          role="group"
          aria-labelledby="appearance-theme"
        >
          {themePreferences.map((preference) => (
            <button
              key={preference}
              type="button"
              className="rs-plan-segment"
              aria-pressed={themePreference === preference}
              onClick={() => setThemePreference(preference)}
            >
              {copy.theme[preference]}
            </button>
          ))}
        </div>
      </div>
      <fieldset className="rs-appearance-field rs-appearance-schemes">
        <legend className="rs-appearance-label">
          {copy.colorScheme.label}
        </legend>
        <div className="rs-scheme-options">
          {colorSchemes.map((scheme) => {
            const words = copy.colorScheme.schemes[scheme];
            return (
              <label
                key={scheme}
                className="rs-scheme-option"
                data-chosen={scheme === colorScheme || undefined}
              >
                <input
                  type="radio"
                  name="color-scheme"
                  value={scheme}
                  checked={scheme === colorScheme}
                  onChange={() => choose(scheme)}
                />
                <span
                  className="rs-scheme-preview"
                  data-color-scheme={scheme}
                  data-theme={theme}
                  aria-hidden="true"
                >
                  <span className="rs-scheme-preview-case">
                    {previewCloths.map((cloth, index) => (
                      <span
                        key={cloth}
                        className="rs-scheme-preview-book"
                        style={{
                          background: cloth,
                          height: `${[70, 56, 82, 62, 74][index]}%`,
                        }}
                      />
                    ))}
                  </span>
                  <span className="rs-scheme-preview-plank" />
                  <span className="rs-scheme-preview-ui">
                    <span className="rs-scheme-preview-accent" />
                    <span className="rs-scheme-preview-fill" />
                  </span>
                </span>
                <span className="rs-scheme-name">{words.name}</span>
                <span className="rs-scheme-description">
                  {words.description}
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>
      {error ? (
        <p className="rs-linked-notice" data-tone="error" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
