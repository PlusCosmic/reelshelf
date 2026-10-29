import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useSetEmail } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";
import { commonCopy } from "@/copy/common";
import { settingsCopy } from "@/copy/settings";

const copy = settingsCopy.email;

/**
 * Lets the user choose where account mail goes. Used both for the first-sign-in step and in Settings.
 * Every account needs an address, so it can be changed but not cleared.
 */
export function EmailField({
  initialEmail,
  submitLabel = commonCopy.save,
  onSaved,
}: {
  initialEmail: string | null;
  submitLabel?: string;
  onSaved?: () => void;
}) {
  const [value, setValue] = useState(initialEmail ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const setEmail = useSetEmail();

  useEffect(() => {
    setValue(initialEmail ?? "");
  }, [initialEmail]);

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    setError(null);
    setSaved(false);
    setEmail.mutate(value, {
      onSuccess: () => {
        setSaved(true);
        onSaved?.();
      },
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
        <label htmlFor="account-email">{copy.label}</label>
        <input
          id="account-email"
          type="email"
          name="email"
          autoComplete="email"
          placeholder={copy.placeholder}
          required
          value={value}
          onChange={(event) => setValue(event.target.value)}
          disabled={setEmail.isPending}
        />
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
          disabled={setEmail.isPending}
        >
          {submitLabel}
        </button>
      </div>
    </form>
  );
}
