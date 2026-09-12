// @vitest-environment jsdom
import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  useInfiniteScroll,
  type InfiniteScrollTarget,
} from "./useInfiniteScroll";

type ObserverEntry = { target: Element; isIntersecting: boolean };

class FakeIntersectionObserver {
  static live: FakeIntersectionObserver[] = [];
  private elements = new Set<Element>();

  constructor(private callback: (entries: ObserverEntry[]) => void) {
    FakeIntersectionObserver.live.push(this);
  }

  observe(element: Element) {
    this.elements.add(element);
  }

  unobserve(element: Element) {
    this.elements.delete(element);
  }

  disconnect() {
    this.elements.clear();
    FakeIntersectionObserver.live = FakeIntersectionObserver.live.filter(
      (observer) => observer !== this,
    );
  }

  /** Stands in for the sentinel scrolling into view. */
  static scrollSentinelIntoView() {
    for (const observer of [...FakeIntersectionObserver.live]) {
      observer.callback(
        [...observer.elements].map((target) => ({
          target,
          isIntersecting: true,
        })),
      );
    }
  }
}

function target(overrides: Partial<InfiniteScrollTarget> = {}) {
  return {
    hasNextPage: true,
    isFetchingNextPage: false,
    isFetchNextPageError: false,
    fetchNextPage: vi.fn(),
    ...overrides,
  };
}

function mountSentinel(props: InfiniteScrollTarget) {
  vi.stubGlobal("IntersectionObserver", FakeIntersectionObserver);
  const view = renderHook(
    (next: InfiniteScrollTarget) => useInfiniteScroll(next),
    {
      initialProps: props,
    },
  );
  act(() => view.result.current.sentinelRef(document.createElement("div")));
  return view;
}

afterEach(() => {
  FakeIntersectionObserver.live = [];
  vi.unstubAllGlobals();
});

describe("useInfiniteScroll", () => {
  it("fetches the next page when the sentinel comes into view", () => {
    const props = target();
    mountSentinel(props);

    act(() => FakeIntersectionObserver.scrollSentinelIntoView());

    expect(props.fetchNextPage).toHaveBeenCalledTimes(1);
  });

  it("keeps loading while the sentinel stays in view", () => {
    const props = target();
    const view = mountSentinel(props);

    act(() => FakeIntersectionObserver.scrollSentinelIntoView());
    // The page arrives without pushing the sentinel off screen, as a short page would.
    view.rerender({ ...props, isFetchingNextPage: true });
    view.rerender({ ...props, isFetchingNextPage: false });
    act(() => FakeIntersectionObserver.scrollSentinelIntoView());

    expect(props.fetchNextPage).toHaveBeenCalledTimes(2);
  });

  it("does not stack requests while a page is in flight", () => {
    const props = target({ isFetchingNextPage: true });
    mountSentinel(props);

    act(() => FakeIntersectionObserver.scrollSentinelIntoView());

    expect(props.fetchNextPage).not.toHaveBeenCalled();
  });

  it("stops after a page fails, leaving the retry to the reader", () => {
    const props = target({ isFetchNextPageError: true });
    mountSentinel(props);

    act(() => FakeIntersectionObserver.scrollSentinelIntoView());

    expect(props.fetchNextPage).not.toHaveBeenCalled();
  });

  it("does not observe once there are no more pages", () => {
    const props = target({ hasNextPage: false });
    mountSentinel(props);

    act(() => FakeIntersectionObserver.scrollSentinelIntoView());

    expect(props.fetchNextPage).not.toHaveBeenCalled();
  });

  it("reports no auto-loading without IntersectionObserver", () => {
    vi.stubGlobal("IntersectionObserver", undefined);
    const { result } = renderHook(() => useInfiniteScroll(target()));

    expect(result.current.autoLoads).toBe(false);
  });
});
