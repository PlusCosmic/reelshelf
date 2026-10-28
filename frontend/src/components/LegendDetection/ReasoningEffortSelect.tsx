import { Select } from "@/components/ui";
import { runsCopy } from "@/copy/runs";
import { useLegendReasoningEfforts } from "@/hooks/queries";

/** Picks a reasoning effort for new runs; the empty value is the configured default. */
export function ReasoningEffortSelect({
  value,
  onValueChange,
}: {
  value: string;
  onValueChange: (value: string) => void;
}) {
  const efforts = useLegendReasoningEfforts();
  return (
    <Select
      className="rs-legend-effort"
      aria-label={runsCopy.reasoningEffort.label}
      value={value}
      onValueChange={onValueChange}
      options={[
        { value: "", label: runsCopy.reasoningEffort.default },
        ...(efforts.data ?? []).map((effort) => ({
          value: effort,
          label: runsCopy.reasoningEffort.option(effort),
        })),
      ]}
    />
  );
}
