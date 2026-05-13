import type { ComponentPropsWithRef } from "react";
import { cx } from "./cx";

type CheckboxProps = ComponentPropsWithRef<"input">;

export function Checkbox({ className, ref, ...props }: CheckboxProps) {
  return (
    <input
      className={cx("rs-ui-checkbox", className)}
      ref={ref}
      {...props}
      type="checkbox"
    />
  );
}
