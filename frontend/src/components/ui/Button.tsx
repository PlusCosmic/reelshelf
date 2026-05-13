import type { ComponentPropsWithoutRef } from "react";
import { cx } from "./cx";

type ButtonProps = ComponentPropsWithoutRef<"button"> & {
  size?: "sm" | "md";
  variant?: "primary" | "secondary" | "ghost";
};

export function Button({
  className,
  size = "md",
  type = "button",
  variant = "secondary",
  ...props
}: ButtonProps) {
  return (
    <button
      className={cx(
        "rs-ui-button",
        `rs-ui-button-${variant}`,
        `rs-ui-button-${size}`,
        className,
      )}
      type={type}
      {...props}
    />
  );
}
