import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "./apiError";
import { fetchMe } from "./user";

const mocks = vi.hoisted(() => ({ getMe: vi.fn() }));

vi.mock("./apiClients", () => ({
  createUserApi: () => ({ getMe: mocks.getMe }),
}));

afterEach(() => {
  vi.clearAllMocks();
});

describe("fetchMe", () => {
  it("answers null when the API says there is no session", async () => {
    mocks.getMe.mockRejectedValueOnce(
      new ApiError("Unauthorized", 401, "auth"),
    );
    await expect(fetchMe()).resolves.toBeNull();
  });

  it("throws on any other failure, which says nothing about being signed in", async () => {
    mocks.getMe.mockRejectedValueOnce(
      new ApiError("Bad Gateway", 502, "unexpected"),
    );
    await expect(fetchMe()).rejects.toBeInstanceOf(ApiError);

    mocks.getMe.mockRejectedValueOnce(new TypeError("Failed to fetch"));
    await expect(fetchMe()).rejects.toBeInstanceOf(TypeError);
  });

  it("maps the signed-in account", async () => {
    mocks.getMe.mockResolvedValueOnce({
      id: "u1",
      username: "ash",
      globalName: null,
      avatar: null,
      email: "ash@example.com",
      needsOnboarding: false,
      needsUsername: false,
      canSetUsername: false,
      isAdmin: false,
    });
    await expect(fetchMe()).resolves.toMatchObject({ id: "u1" });
  });
});
