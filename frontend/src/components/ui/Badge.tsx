import type { ComponentPropsWithoutRef } from "react";
import { cx } from "./cx";

type BadgeProps = ComponentPropsWithoutRef<"span"> & {
  tone?: "accent" | "danger" | "neutral";
};

export function Badge({ className, tone = "neutral", ...props }: BadgeProps) {
  return (
    <span
      className={cx("rs-ui-badge", `rs-ui-badge-${tone}`, className)}
      {...props}
    />
  );
}
