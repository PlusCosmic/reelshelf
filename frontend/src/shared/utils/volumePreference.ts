export type VolumePreference = { volume: number; muted: boolean };

const defaultPreference: VolumePreference = { volume: 1, muted: false };

/** A viewer's volume, remembered in their browser under `key`; full volume when nothing is saved or storage is blocked. */
export function loadVolume(key: string): VolumePreference {
  try {
    const saved = JSON.parse(
      window.localStorage.getItem(key) ?? "null",
    ) as Partial<VolumePreference> | null;
    const volume = Number(saved?.volume);
    return {
      volume: Number.isFinite(volume) ? Math.min(1, Math.max(0, volume)) : 1,
      muted: saved?.muted === true,
    };
  } catch {
    return defaultPreference;
  }
}

export function saveVolume(key: string, preference: VolumePreference) {
  try {
    window.localStorage.setItem(key, JSON.stringify(preference));
  } catch {
    // Storage blocked (private window, previews): the setting lasts until the page closes.
  }
}
