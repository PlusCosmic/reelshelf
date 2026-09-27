import { Select } from "@/components/ui";
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
      aria-label="Reasoning effort"
      value={value}
      onValueChange={onValueChange}
      options={[
        { value: "", label: "Default reasoning" },
        ...(efforts.data ?? []).map((effort) => ({
          value: effort,
          label: `${effort} reasoning`,
        })),
      ]}
    />
  );
}
