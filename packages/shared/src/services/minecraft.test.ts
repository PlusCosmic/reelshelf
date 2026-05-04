import { describe, expect, it } from "vitest";
import { createConsoleWebSocketUrl } from "./minecraft";

describe("createConsoleWebSocketUrl", () => {
  it("turns a relative API base URL into an absolute same-origin WebSocket URL", () => {
    expect(createConsoleWebSocketUrl("/api", "server-1", "https://minecraft.pluscosmic.dev")).toBe(
      "wss://minecraft.pluscosmic.dev/api/minecraft/servers/server-1/console/live",
    );
  });

  it("converts absolute HTTP API origins to WebSocket origins", () => {
    expect(createConsoleWebSocketUrl("http://localhost:8080/api", "server-2", "https://app.example")).toBe(
      "ws://localhost:8080/api/minecraft/servers/server-2/console/live",
    );
  });
});
