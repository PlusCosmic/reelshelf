import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import { IconChevronDown } from "@tabler/icons-react";
import { cx } from "./cx";

export type SelectOption = {
  disabled?: boolean;
  label: string;
  value: string;
};

type SelectProps = {
  "aria-label"?: string;
  className?: string;
  disabled?: boolean;
  options: SelectOption[];
  placeholder?: string;
  value: string;
  onValueChange: (value: string) => void;
};

type SelectPosition = {
  left: number;
  top: number;
  width: number;
};

export function Select({
  "aria-label": ariaLabel,
  className,
  disabled = false,
  options,
  placeholder = "Select",
  value,
  onValueChange,
}: SelectProps) {
  const id = useId();
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [activeValue, setActiveValue] = useState("");
  const [position, setPosition] = useState<SelectPosition | null>(null);
  const selectedOption = options.find((option) => option.value === value);
  const enabledOptions = useMemo(
    () => options.filter((option) => !option.disabled),
    [options],
  );

  useLayoutEffect(() => {
    if (!open) return;
    const updatePosition = () => {
      const rect = triggerRef.current?.getBoundingClientRect();
      if (!rect) return;
      setPosition({
        left: rect.left,
        top: rect.bottom + 6,
        width: rect.width,
      });
    };

    updatePosition();
    window.addEventListener("resize", updatePosition);
    window.addEventListener("scroll", updatePosition, true);
    return () => {
      window.removeEventListener("resize", updatePosition);
      window.removeEventListener("scroll", updatePosition, true);
    };
  }, [open]);

  useEffect(() => {
    if (!open) return;
    setActiveValue(value || enabledOptions[0]?.value || "");

    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node;
      if (
        triggerRef.current?.contains(target) ||
        listRef.current?.contains(target)
      ) {
        return;
      }
      setOpen(false);
    };

    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [enabledOptions, open, value]);

  function moveActive(direction: 1 | -1) {
    if (!enabledOptions.length) return;
    const currentIndex = Math.max(
      0,
      enabledOptions.findIndex((option) => option.value === activeValue),
    );
    const next =
      enabledOptions[
        (currentIndex + direction + enabledOptions.length) %
          enabledOptions.length
      ];
    setActiveValue(next.value);
  }

  function commit(nextValue: string) {
    onValueChange(nextValue);
    setOpen(false);
    triggerRef.current?.focus();
  }

  return (
    <>
      <button
        ref={triggerRef}
        aria-activedescendant={open ? `${id}-${activeValue}` : undefined}
        aria-controls={`${id}-listbox`}
        aria-expanded={open}
        aria-haspopup="listbox"
        aria-label={ariaLabel}
        className={cx("rs-ui-select-trigger", className)}
        disabled={disabled}
        type="button"
        onClick={() => setOpen((current) => !current)}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown") {
            event.preventDefault();
            if (!open) setOpen(true);
            else moveActive(1);
          }
          if (event.key === "ArrowUp") {
            event.preventDefault();
            if (!open) setOpen(true);
            else moveActive(-1);
          }
          if (event.key === "Enter" || event.key === " ") {
            event.preventDefault();
            if (!open) setOpen(true);
            else commit(activeValue);
          }
          if (event.key === "Escape") {
            setOpen(false);
          }
        }}
      >
        <span
          className={selectedOption ? undefined : "rs-ui-select-placeholder"}
        >
          {selectedOption?.label ?? placeholder}
        </span>
        <IconChevronDown size={15} />
      </button>

      {open && position
        ? createPortal(
            <div
              ref={listRef}
              className="rs-ui-select-list"
              id={`${id}-listbox`}
              role="listbox"
              style={{
                left: position.left,
                top: position.top,
                width: position.width,
              }}
            >
              {options.map((option) => (
                <button
                  id={`${id}-${option.value}`}
                  className={cx(
                    "rs-ui-select-option",
                    option.value === value && "selected",
                    option.value === activeValue && "active",
                  )}
                  disabled={option.disabled}
                  key={option.value}
                  role="option"
                  type="button"
                  aria-selected={option.value === value}
                  onClick={() => commit(option.value)}
                  onMouseEnter={() => setActiveValue(option.value)}
                >
                  {option.label}
                </button>
              ))}
            </div>,
            document.body,
          )
        : null}
    </>
  );
}
