// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/shared/services/apiError";
import { DeviceApproval } from "./DeviceApproval";

type Decide = (
  variables: { code: string; decision: "allow" | "deny" },
  options: { onSuccess: () => void; onError: (error: unknown) => void },
) => void;

const mocks = vi.hoisted(() => ({
  request: {
    isLoading: false,
    isError: false,
    data: null as { deviceName: string; expiresAt: Date } | null,
  },
  decide: vi.fn<Decide>(),
}));

vi.mock("@/hooks/devices.queries", () => ({
  useDeviceAuthorization: () => mocks.request,
  useDecideDeviceAuthorization: () => ({
    mutate: mocks.decide,
    isPending: false,
    variables: undefined,
  }),
}));

afterEach(() => {
  cleanup();
  mocks.decide.mockReset();
  mocks.request = { isLoading: false, isError: false, data: null };
});

function pending() {
  mocks.request = {
    isLoading: false,
    isError: false,
    data: { deviceName: "Gaming PC", expiresAt: new Date() },
  };
}

describe("DeviceApproval", () => {
  it("shows a waiting sign-in with its device and code", () => {
    pending();
    render(<DeviceApproval code="KDRW-TPLM" onCode={vi.fn()} />);

    expect(screen.getByText("Sign in a device")).toBeTruthy();
    expect(
      screen.getByRole("heading", {
        name: 'Let "Gaming PC" add clips to your library?',
      }),
    ).toBeTruthy();
    expect(screen.getByText("KDRW-TPLM")).toBeTruthy();
    expect(screen.getByText(/Check this matches the code/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Allow" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Don't allow" })).toBeTruthy();
  });

  it("says when a code matches no sign-in", () => {
    render(<DeviceApproval code="BBBB-BBBB" onCode={vi.fn()} />);

    expect(
      screen.getByText(
        "That code didn't match a sign-in, so nothing changed. Check the code in the Reelshelf app, or get a new one there.",
      ),
    ).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Allow" })).toBeNull();
  });

  it("signs the device in on Allow", () => {
    pending();
    mocks.decide.mockImplementation((_, options) => options.onSuccess());
    render(<DeviceApproval code="KDRW-TPLM" onCode={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Allow" }));

    expect(mocks.decide).toHaveBeenCalledWith(
      { code: "KDRW-TPLM", decision: "allow" },
      expect.anything(),
    );
    expect(
      screen.getByText("Gaming PC is signed in. You can close this tab."),
    ).toBeTruthy();
  });

  it("turns the device away on Don't allow", () => {
    pending();
    mocks.decide.mockImplementation((_, options) => options.onSuccess());
    render(<DeviceApproval code="KDRW-TPLM" onCode={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Don't allow" }));

    expect(mocks.decide).toHaveBeenCalledWith(
      { code: "KDRW-TPLM", decision: "deny" },
      expect.anything(),
    );
    expect(
      screen.getByText("Gaming PC wasn't signed in. Nothing changed."),
    ).toBeTruthy();
  });

  it("treats a code that expired meanwhile as unknown", () => {
    pending();
    mocks.decide.mockImplementation((_, options) =>
      options.onError(new ApiError("Not found", 404, "not-found")),
    );
    render(<DeviceApproval code="KDRW-TPLM" onCode={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Allow" }));

    expect(screen.getByText(/That code didn't match a sign-in/)).toBeTruthy();
  });

  it("asks for the code when the address has none", () => {
    const onCode = vi.fn();
    render(<DeviceApproval code={undefined} onCode={onCode} />);

    fireEvent.change(
      screen.getByLabelText("Enter the code shown in the Reelshelf app"),
      { target: { value: "kdrwtplm" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));

    expect(onCode).toHaveBeenCalledWith("KDRW-TPLM");
  });
});
