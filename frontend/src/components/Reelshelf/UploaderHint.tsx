import { deviceCopy, uploaderStoreUrl } from "@/copy/device";
import { useFeature } from "@/hooks/featureFlags.queries";
import { isWindowsDesktop } from "@/shared/services/devices";

/**
 * "Or upload automatically while you play", the link to the desktop uploader's Store listing beside the library's
 * drop-on-a-book prompt. Only Windows desktop browsers get it, and only with the desktop-uploader flag.
 */
export function UploaderHint() {
  const enabled = useFeature("desktop-uploader");
  if (!enabled || !isWindowsDesktop()) return null;

  return (
    <a
      className="rs-uploader-hint"
      href={uploaderStoreUrl}
      target="_blank"
      rel="noopener noreferrer"
    >
      {deviceCopy.libraryHint}
    </a>
  );
}
