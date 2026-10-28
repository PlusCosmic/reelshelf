import { useState } from "react";
import { Input, Select } from "@/components/ui";
import { runsCopy } from "@/copy/runs";
import { useLegendProviders } from "@/hooks/queries";
import {
  decisionsProviders,
  type LegendRunOptions,
} from "@/shared/services/legendDetection";
import { ReasoningEffortSelect } from "./ReasoningEffortSelect";

/** The provider, model and reasoning effort fields, as typed; empty means the configured default. */
export function useRunOptions() {
  const [provider, setProvider] = useState("");
  const [model, setModel] = useState("");
  const [reasoningEffort, setReasoningEffort] = useState("");
  const takesEffort = !decisionsProviders.includes(provider);
  const options: LegendRunOptions = {
    provider: provider || null,
    model: model.trim() || null,
    reasoningEffort: takesEffort ? reasoningEffort || null : null,
  };
  return {
    options,
    provider,
    model: model.trim(),
    takesEffort,
    fields: (
      <>
        <ProviderSelect value={provider} onValueChange={setProvider} />
        <Input
          compact
          value={model}
          onChange={(event) => setModel(event.target.value)}
          placeholder={runsCopy.rerun.modelPlaceholder}
          aria-label={runsCopy.rerun.modelLabel}
        />
        {takesEffort ? (
          <ReasoningEffortSelect
            value={reasoningEffort}
            onValueChange={setReasoningEffort}
          />
        ) : null}
      </>
    ),
  };
}

/** Picks a provider for new runs; the empty value is the configured default. */
function ProviderSelect({
  value,
  onValueChange,
}: {
  value: string;
  onValueChange: (value: string) => void;
}) {
  const providers = useLegendProviders();
  return (
    <Select
      className="rs-legend-effort"
      aria-label={runsCopy.provider.label}
      value={value}
      onValueChange={onValueChange}
      options={[
        { value: "", label: runsCopy.provider.default },
        ...(providers.data ?? []).map((provider) => ({
          value: provider,
          label: provider,
        })),
      ]}
    />
  );
}
