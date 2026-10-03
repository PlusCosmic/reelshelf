import { useState } from "react";
import type { CSSProperties } from "react";

export function Avatar({
  name = "You",
  src,
  size = 28,
  color,
}: {
  name?: string | null;
  src?: string | null;
  size?: number;
  /** Background behind the initials; the accent fill when unset. Text stays white, so pass a mid-tone. */
  color?: string;
}) {
  const [imageFailed, setImageFailed] = useState(false);
  const initials = (name ?? "You")
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
  return (
    <span
      className="rs-avatar"
      style={{ "--size": `${size}px`, background: color } as CSSProperties}
    >
      {src && !imageFailed ? (
        <img
          src={src}
          alt={name ?? "User avatar"}
          onError={() => setImageFailed(true)}
        />
      ) : (
        initials || "Y"
      )}
    </span>
  );
}
