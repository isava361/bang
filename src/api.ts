import type { GameStateView } from "./types.ts";
import { state } from "./state.ts";
import { gamePanel } from "./dom.ts";

let updateStateFn: ((s: GameStateView) => void) | null = null;

export function setUpdateStateHandler(fn: (s: GameStateView) => void): void {
  updateStateFn = fn;
}

export const apiPost = async <T = unknown>(url: string, body: unknown): Promise<T> => {
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  const payload = await response.json();
  if (!response.ok) {
    throw new Error(payload.message || "Запрос не выполнен.");
  }

  return payload.data as T;
};

export const syncState = async (): Promise<void> => {
  if (!state.playerId) return;
  try {
    const response = await fetch("/api/state");
    const payload = await response.json();
    if (response.ok && payload.data && updateStateFn) updateStateFn(payload.data);
  } catch {
    // silent
  }
};

export const startStatePolling = (): void => {
  if (state.statePollTimer) return;
  state.statePollTimer = setInterval(() => {
    if (!state.playerId || gamePanel.classList.contains("hidden")) return;
    syncState();
  }, 1200);
};

export const stopStatePolling = (): void => {
  if (!state.statePollTimer) return;
  clearInterval(state.statePollTimer);
  state.statePollTimer = null;
};
