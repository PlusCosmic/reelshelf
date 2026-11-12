import { Notice } from "@pluscosmic/ui";
import { useState } from "react";
import type { FormEvent } from "react";
import { deviceCopy } from "@/copy/device";
import {
  useDecideDeviceAuthorization,
  useDeviceAuthorization,
} from "@/hooks/devices.queries";
import { ApiError } from "@/shared/services/apiError";
import {
  type DeviceDecision,
  formatDeviceCode,
} from "@/shared/services/devices";

const copy = deviceCopy.approve;

/**
 * /device (W1): the sign-in the desktop uploader or the monitor app asked for (saying which), approved or turned down by the signed-in owner. Without a
 * code it asks for the one the uploader shows.
 */
export function DeviceApproval({
  code,
  onCode,
}: {
  code: string | undefined;
  onCode: (code: string) => void;
}) {
  return (
    <section className="rs-section rs-device-page">
      <span className="rs-eyebrow">{copy.eyebrow}</span>
      {code ? (
        <DeviceRequest key={code} code={code} />
      ) : (
        <DeviceCodeForm onCode={onCode} />
      )}
    </section>
  );
}

function DeviceCodeForm({ onCode }: { onCode: (code: string) => void }) {
  const [value, setValue] = useState("");

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    if (value.trim()) onCode(formatDeviceCode(value));
  };

  return (
    <form className="rs-form rs-device-code-form" onSubmit={handleSubmit}>
      <div className="rs-field">
        <label htmlFor="device-code">{copy.codeLabel}</label>
        <input
          id="device-code"
          name="code"
          autoComplete="one-time-code"
          autoCapitalize="characters"
          spellCheck={false}
          placeholder={copy.codePlaceholder}
          required
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
      </div>
      <div className="rs-email-actions">
        <button className="rs-primary" type="submit">
          {copy.continue}
        </button>
      </div>
    </form>
  );
}

type Outcome = { decision: DeviceDecision } | { error: string } | null;

function DeviceRequest({ code }: { code: string }) {
  const request = useDeviceAuthorization(code);
  const decide = useDecideDeviceAuthorization();
  const [outcome, setOutcome] = useState<Outcome>(null);

  if (request.isLoading) {
    return (
      <p className="rs-sidebar-copy" role="status">
        {copy.loading}
      </p>
    );
  }

  if (request.isError) {
    return (
      <Notice compact tone="error">
        {copy.loadFailed}
      </Notice>
    );
  }

  const device = request.data;
  if (
    !device ||
    (outcome && "error" in outcome && outcome.error === copy.unknown)
  ) {
    return (
      <Notice compact tone="error">
        {copy.unknown}
      </Notice>
    );
  }

  if (outcome && "decision" in outcome) {
    return (
      <Notice
        compact
        tone={outcome.decision === "allow" ? "success" : "warning"}
      >
        {outcome.decision === "allow"
          ? copy.allowed(device.deviceName)
          : copy.denied(device.deviceName)}
      </Notice>
    );
  }

  const answer = (decision: DeviceDecision) => {
    setOutcome(null);
    decide.mutate(
      { code, decision },
      {
        onSuccess: () => setOutcome({ decision }),
        onError: (error) =>
          setOutcome({
            // The request expired, or was answered elsewhere, meanwhile.
            error:
              error instanceof ApiError &&
              (error.status === 404 || error.status === 409)
                ? copy.unknown
                : copy.failed,
          }),
      },
    );
  };

  return (
    <>
      <p className="rs-sidebar-copy">{copy.app(device.client)}</p>
      <h1 className="rs-display rs-h2">
        {copy.title(device.deviceName, device.client)}
      </h1>
      <p className="rs-device-code">{formatDeviceCode(code)}</p>
      <p className="rs-sidebar-copy">{copy.body(device.client)}</p>
      {outcome && "error" in outcome ? (
        <Notice compact tone="error">
          {outcome.error}
        </Notice>
      ) : null}
      <div className="rs-device-actions">
        <button
          className="rs-primary"
          type="button"
          disabled={decide.isPending}
          onClick={() => answer("allow")}
        >
          {decide.isPending && decide.variables?.decision === "allow"
            ? copy.working
            : copy.allow}
        </button>
        <button
          className="rs-secondary"
          type="button"
          disabled={decide.isPending}
          onClick={() => answer("deny")}
        >
          {copy.deny}
        </button>
      </div>
    </>
  );
}
