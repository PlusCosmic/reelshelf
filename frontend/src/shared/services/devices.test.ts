import { describe, expect, it } from "vitest";
import {
  formatDeviceCode,
  isWindowsDesktop,
  normalizeDeviceCode,
} from "./devices";

describe("device codes", () => {
  it("are matched ignoring case, spaces and the dash", () => {
    expect(normalizeDeviceCode(" kdrw-tplm ")).toBe("KDRWTPLM");
    expect(formatDeviceCode("kdrwtplm")).toBe("KDRW-TPLM");
    expect(formatDeviceCode("KDRW TPLM")).toBe("KDRW-TPLM");
  });
});

describe("isWindowsDesktop", () => {
  const nav = (platform: string, uaPlatform?: string) =>
    ({
      platform,
      userAgentData:
        uaPlatform === undefined ? undefined : { platform: uaPlatform },
    }) as unknown as Navigator;

  it("offers the uploader to Windows browsers with a mouse", () => {
    expect(isWindowsDesktop(nav("", "Windows"), false)).toBe(true);
    expect(isWindowsDesktop(nav("Win32"), false)).toBe(true);
  });

  it("doesn't offer it elsewhere, or on touch-first Windows devices", () => {
    expect(isWindowsDesktop(nav("MacIntel", "macOS"), false)).toBe(false);
    expect(isWindowsDesktop(nav("Linux x86_64"), false)).toBe(false);
    expect(isWindowsDesktop(nav("Win32", "Windows"), true)).toBe(false);
    expect(isWindowsDesktop(undefined, false)).toBe(false);
  });
});
