import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useSetUsername } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import { commonCopy } from "@/copy/common";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.username;

/**
 * Lets the owner of an account that picks its own username (one whose primary sign-in is Google) set it.
 * Used both for the first-sign-in step and in Settings. The API checks the rules and that nobody else has it.
 */
export function UsernameField({
  initialUsername,
  submitLabel = commonCopy.save,
}: {
  initialUsername: string;
  submitLabel?: string;
}) {
  const [value, setValue] = useState(initialUsername);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const setUsername = useSetUsername();

  useEffect(() => {
    setValue(initialUsername);
  }, [initialUsername]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSaved(false);
    setUsername.mutate(value, {
      onSuccess: () => setSaved(true),
      onError: (failure) => {
        setError(
          failure instanceof ApiError ? failure.message : copy.saveFailed,
        );
      },
    });
  };

  return (
    <form className="rs-form rs-email-form" onSubmit={handleSubmit}>
      <div className="rs-field">
        <label htmlFor="account-username">{copy.label}</label>
        <input
          id="account-username"
          type="text"
          name="username"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          required
          minLength={2}
          maxLength={32}
          value={value}
          onChange={(event) => setValue(event.target.value)}
          disabled={setUsername.isPending}
          aria-describedby="account-username-hint"
        />
        <p id="account-username-hint" className="rs-field-hint">
          {copy.hint}
        </p>
      </div>
      {error ? (
        <p className="rs-linked-notice" data-tone="error" role="alert">
          {error}
        </p>
      ) : null}
      {saved && !error ? (
        <p className="rs-linked-notice" data-tone="success" role="status">
          {copy.saved}
        </p>
      ) : null}
      <div className="rs-email-actions">
        <button
          className="rs-primary"
          type="submit"
          disabled={setUsername.isPending}
        >
          {submitLabel}
        </button>
      </div>
    </form>
  );
}
