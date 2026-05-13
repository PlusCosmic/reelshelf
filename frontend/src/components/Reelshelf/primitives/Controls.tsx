import type { ReactNode } from "react";
import { IconSearch } from "@tabler/icons-react";

export function Chip({
  children,
  active,
  onClick,
}: {
  children: ReactNode;
  active?: boolean;
  onClick?: () => void;
}) {
  return (
    <button
      className={`rs-chip${active ? " active" : ""}`}
      type="button"
      onClick={onClick}
    >
      {children}
    </button>
  );
}

export function SearchBox({
  value,
  onChange,
  placeholder = "Search clips, games, tags…",
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  return (
    <label className="rs-search">
      <IconSearch size={15} />
      <input
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        placeholder={placeholder}
      />
    </label>
  );
}
