import type { ComponentPropsWithoutRef, ReactNode } from "react";
import { cx } from "./cx";

type FieldProps = ComponentPropsWithoutRef<"div"> & {
  label: ReactNode;
};

export function Field({ children, className, label, ...props }: FieldProps) {
  return (
    <div className={cx("rs-ui-field", className)} {...props}>
      <span className="rs-ui-field-label">{label}</span>
      {children}
    </div>
  );
}
