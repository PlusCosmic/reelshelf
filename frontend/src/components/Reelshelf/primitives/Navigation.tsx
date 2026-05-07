import { Link } from "@tanstack/react-router";
import {
  IconChevronLeft,
  IconClock,
  IconDownload,
  IconEye,
  IconFolderPlus,
  IconPlus,
  IconShare3,
} from "@tabler/icons-react";

export function PlayerActions() {
  return (
    <div className="rs-action-row">
      <button className="rs-small-button" type="button">
        <IconShare3 size={13} />
        Share
      </button>
      <button className="rs-small-button" type="button">
        <IconFolderPlus size={13} />
        Add to collection
      </button>
      <button className="rs-small-button" type="button">
        <IconDownload size={13} />
        Download
      </button>
    </div>
  );
}

export function BackToLibrary({ to = "/" }: { to?: string }) {
  return (
    <Link to={to} className="rs-small-button rs-back-link">
      <IconChevronLeft size={14} />
      Library
    </Link>
  );
}

export const Icons = {
  eye: IconEye,
  clock: IconClock,
  plus: IconPlus,
};
