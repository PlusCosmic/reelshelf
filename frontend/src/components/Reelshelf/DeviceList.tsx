import { Notice } from "@pluscosmic/ui";
import { useState } from "react";
import { deviceCopy, uploaderStoreUrl } from "@/copy/device";
import { useDevices, useRemoveDevice } from "@/hooks/devices.queries";
import type { DeviceSummary } from "@/shared/services/devices";

const copy = deviceCopy.devices;

/**
 * Settings → Devices (W2): the computers running the desktop uploader, each removable after an inline confirmation,
 * and the link to the uploader's Store listing.
 */
export function DeviceList({ now }: { now?: Date }) {
  const devices = useDevices();

  return (
    <div className="rs-linked-accounts">
      {devices.isLoading ? (
        <p className="rs-sidebar-copy">{copy.loading}</p>
      ) : null}
      {devices.isError ? (
        <Notice compact tone="error">
          {copy.loadFailed}
        </Notice>
      ) : null}
      {devices.data && devices.data.length === 0 ? (
        <p className="rs-sidebar-copy">{copy.empty}</p>
      ) : null}
      {devices.data && devices.data.length > 0 ? (
        <ul className="rs-linked-list">
          {devices.data.map((device) => (
            <DeviceRow key={device.id} device={device} now={now} />
          ))}
        </ul>
      ) : null}
      <p className="rs-sidebar-copy">
        {copy.store}{" "}
        <a href={uploaderStoreUrl} target="_blank" rel="noopener noreferrer">
          {copy.storeLink}
        </a>
      </p>
    </div>
  );
}

function DeviceRow({ device, now }: { device: DeviceSummary; now?: Date }) {
  const remove = useRemoveDevice();
  const [confirming, setConfirming] = useState(false);
  const [failed, setFailed] = useState(false);

  const handleRemove = () => {
    setFailed(false);
    remove.mutate(device.id, {
      onError: () => {
        setFailed(true);
        setConfirming(false);
      },
    });
  };

  return (
    <li className="rs-linked-row">
      <span className="rs-linked-meta">
        <span className="rs-linked-name">{device.name}</span>
        <span className="rs-linked-sub">
          {copy.used(device.createdAt, device.lastUsedAt, now)}
        </span>
        {confirming ? (
          <span className="rs-device-confirm" role="alert">
            {copy.confirm(device.name)}
          </span>
        ) : null}
        {failed ? (
          <span className="ui-notice-compact" data-tone="error" role="alert">
            {copy.removeFailed(device.name)}
          </span>
        ) : null}
      </span>
      {confirming ? (
        <span className="rs-device-actions">
          <button
            className="rs-primary"
            type="button"
            disabled={remove.isPending}
            onClick={handleRemove}
          >
            {remove.isPending ? copy.removing : copy.remove}
          </button>
          <button
            className="rs-secondary"
            type="button"
            disabled={remove.isPending}
            onClick={() => setConfirming(false)}
          >
            {copy.cancel}
          </button>
        </span>
      ) : (
        <button
          className="rs-secondary"
          type="button"
          onClick={() => setConfirming(true)}
        >
          {copy.remove}
        </button>
      )}
    </li>
  );
}
