// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DeviceList } from "./DeviceList";

type Device = { id: string; name: string; createdAt: Date; lastUsedAt: Date };

const mocks = vi.hoisted(() => ({
  devices: {
    isLoading: false,
    isError: false,
    data: [] as Device[] | undefined,
  },
  remove: vi.fn(),
}));

vi.mock("@/hooks/devices.queries", () => ({
  useDevices: () => mocks.devices,
  useRemoveDevice: () => ({ mutate: mocks.remove, isPending: false }),
}));

afterEach(() => {
  cleanup();
  mocks.remove.mockReset();
  mocks.devices = { isLoading: false, isError: false, data: [] };
});

const now = new Date("2026-11-06T21:47:00Z");

describe("DeviceList", () => {
  it("lists each device with when it was added and last used", () => {
    mocks.devices.data = [
      {
        id: "a",
        name: "Gaming PC",
        createdAt: new Date("2026-11-06T12:00:00Z"),
        lastUsedAt: new Date("2026-11-06T21:43:00Z"),
      },
      {
        id: "b",
        name: "Laptop",
        createdAt: new Date("2026-10-02T12:00:00Z"),
        lastUsedAt: new Date("2026-10-12T12:00:00Z"),
      },
    ];
    render(<DeviceList now={now} />);

    expect(screen.getByText("Gaming PC")).toBeTruthy();
    expect(
      screen.getByText("Added 6 Nov 2026 · last used 4 minutes ago"),
    ).toBeTruthy();
    expect(screen.getByText("Laptop")).toBeTruthy();
    expect(
      screen.getByText("Added 2 Oct 2026 · last used 12 Oct 2026"),
    ).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Remove" })).toHaveLength(2);
    expect(
      screen.getByRole("link", {
        name: "Get the Windows app from the Microsoft Store",
      }),
    ).toBeTruthy();
  });

  it("says when there are no devices, and still offers the uploader", () => {
    render(<DeviceList now={now} />);

    expect(screen.getByText("No devices yet.")).toBeTruthy();
    expect(
      screen.getByText(/Upload clips automatically while you play/),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", {
        name: "Get the Windows app from the Microsoft Store",
      }),
    ).toBeTruthy();
  });

  it("asks before removing a device, and removes it once confirmed", () => {
    mocks.devices.data = [
      {
        id: "a",
        name: "Gaming PC",
        createdAt: new Date("2026-11-06T12:00:00Z"),
        lastUsedAt: new Date("2026-11-06T21:43:00Z"),
      },
    ];
    render(<DeviceList now={now} />);

    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    expect(
      screen.getByText("Remove Gaming PC? It stops uploading at once."),
    ).toBeTruthy();
    expect(mocks.remove).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(screen.queryByText(/It stops uploading at once/)).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    expect(mocks.remove).toHaveBeenCalledWith("a", expect.anything());
  });
});
