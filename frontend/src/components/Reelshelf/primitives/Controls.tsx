import type { CSSProperties, ReactNode } from "react";
import { IconSearch } from "@tabler/icons-react";
import type { GameCategoryResponse } from "@/api-client";
import { getGameColors } from "../reelshelf-model";

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
  placeholder = "Search clips, games, tags...",
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

export function GameVars({
  category,
  children,
}: {
  category?: GameCategoryResponse | null;
  children: ReactNode;
}) {
  const [colorA, colorB] = getGameColors(category?.id ?? "fallback");
  return (
    <div style={{ "--game-a": colorA, "--game-b": colorB } as CSSProperties}>
      {children}
    </div>
  );
}
