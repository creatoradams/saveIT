"use client";

import { useEffect, useRef, useState } from "react";

// A component releases only its own workspace reservation, including on unmount.
// Screen-specific conflict and authority checks stay with the caller.
export function useOwnedRequest(onBusyChange: (value: boolean) => void) {
  const owned = useRef<AbortController | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(
    () => () => {
      const current = owned.current;
      if (current) {
        owned.current = null;
        current.abort();
        onBusyChange(false);
      }
    },
    [onBusyChange],
  );
  async function run(
    action: (signal: AbortSignal) => Promise<void>,
    fail: (error: unknown) => void,
  ) {
    if (owned.current) return;
    const controller = new AbortController();
    owned.current = controller;
    setBusy(true);
    onBusyChange(true);
    try {
      await action(controller.signal);
    } catch (error) {
      if (!controller.signal.aborted && owned.current === controller)
        fail(error);
    } finally {
      if (!controller.signal.aborted && owned.current === controller) {
        owned.current = null;
        setBusy(false);
        onBusyChange(false);
      }
    }
  }
  return { busy, run };
}
