// @vitest-environment jsdom
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { act, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import { planQueryKey, useCurrentUser, useLogout } from "./auth.queries";

const mocks = vi.hoisted(() => ({
  fetchMe: vi.fn(() => Promise.resolve({ id: "account-1" })),
  logout: vi.fn(() => Promise.resolve()),
}));

vi.mock("@/shared/services/user", () => ({ fetchMe: mocks.fetchMe }));
vi.mock("@/shared/services/auth", () => ({ logout: mocks.logout }));

describe("useLogout", () => {
  it("tells whoever is showing the current user that they signed out", async () => {
    const queryClient = new QueryClient();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    queryClient.setQueryData(planQueryKey, { tier: "pro" });

    const { result } = renderHook(
      () => ({ user: useCurrentUser(), logout: useLogout() }),
      { wrapper },
    );
    await waitFor(() => expect(result.current.user.data).toBeTruthy());

    await act(() => result.current.logout.mutateAsync());

    // Observers hear about cache changes through TanStack's batched notifier, which flushes on a
    // timer; without the fix the user is never set to null, so this times out.
    await waitFor(() => expect(result.current.user.data).toBeNull());
    expect(
      queryClient.getQueryCache().find({ queryKey: planQueryKey }),
    ).toBeUndefined();
  });
});
