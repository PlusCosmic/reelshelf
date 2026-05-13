import type { ComponentPropsWithoutRef } from "react";
import { cx } from "./cx";

type InputProps = ComponentPropsWithoutRef<"input"> & {
  compact?: boolean;
};

export function Input({ className, compact = false, ...props }: InputProps) {
  return (
    <input
      className={cx("rs-ui-input", compact && "rs-ui-input-compact", className)}
      {...props}
    />
  );
}
