import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { useSetEmail } from "@/hooks/queries";
import { ApiError } from "@/shared/services/apiError";

/**
 * Lets the user choose where account mail goes. Used both for the first-sign-in step and in Settings.
 */
export function EmailField({
  initialEmail,
  submitLabel = "Save",
  allowSkip = false,
  onSaved,
}: {
  initialEmail: string | null;
  submitLabel?: string;
  allowSkip?: boolean;
  onSaved?: () => void;
}) {
  const [value, setValue] = useState(initialEmail ?? "");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const setEmail = useSetEmail();

  useEffect(() => {
    setValue(initialEmail ?? "");
  }, [initialEmail]);

  const submit = (email: string) => {
    setError(null);
    setSaved(false);
    setEmail.mutate(email, {
      onSuccess: () => {
        setSaved(true);
        onSaved?.();
      },
      onError: (failure) => {
        setError(
          failure instanceof ApiError
            ? failure.message
            : "Couldn't save that address.",
        );
      },
    });
  };

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    submit(value);
  };

  return (
    <form className="rs-form rs-email-form" onSubmit={handleSubmit}>
      <div className="rs-field">
        <label htmlFor="account-email">Email</label>
        <input
          id="account-email"
          type="email"
          name="email"
          autoComplete="email"
          placeholder="you@example.com"
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
          Saved.
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
        {allowSkip ? (
          <button
            className="rs-secondary"
            type="button"
            disabled={setEmail.isPending}
            onClick={() => submit("")}
          >
            Skip for now
          </button>
        ) : null}
      </div>
    </form>
  );
}
