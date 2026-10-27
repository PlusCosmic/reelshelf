// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ReportClip } from "./ReportClip";

const mocks = vi.hoisted(() => ({
  report: vi.fn(),
  isSuccess: false,
}));

vi.mock("@/hooks/queries", () => ({
  useReportSharedClip: () => ({
    mutate: mocks.report,
    isPending: false,
    isSuccess: mocks.isSuccess,
  }),
}));

afterEach(() => {
  cleanup();
  mocks.report.mockReset();
  mocks.isSuccess = false;
});

describe("ReportClip", () => {
  it("sends nothing until a reason is picked, then sends the reason, details and email", () => {
    render(<ReportClip token="abc" />);
    fireEvent.click(screen.getByRole("button", { name: "Report this clip" }));

    const send = screen.getByRole("button", { name: "Send report" });
    expect(send).toHaveProperty("disabled", true);

    fireEvent.click(screen.getByLabelText("Graphic violence or gore"));
    fireEvent.change(screen.getByRole("textbox", { name: /anything else/i }), {
      target: { value: "real footage" },
    });
    fireEvent.click(send);

    expect(mocks.report).toHaveBeenCalledWith(
      { reason: "violence", details: "real footage", email: "" },
      expect.anything(),
    );
  });

  it("points child safety reports at the IWF and NCMEC", () => {
    render(<ReportClip token="abc" />);
    fireEvent.click(screen.getByRole("button", { name: "Report this clip" }));
    fireEvent.click(
      screen.getByLabelText("Child sexual abuse or exploitation"),
    );

    expect(
      screen.getByRole("link", { name: "Internet Watch Foundation" }),
    ).toBeTruthy();
    expect(
      screen.getByRole("link", { name: "NCMEC's CyberTipline" }),
    ).toBeTruthy();
  });

  it("thanks the reporter once the report is sent", () => {
    mocks.isSuccess = true;
    render(<ReportClip token="abc" />);
    fireEvent.click(screen.getByRole("button", { name: "Report this clip" }));

    expect(screen.getByText("Thanks for telling us")).toBeTruthy();
  });
});
