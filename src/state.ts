import type { GameStateView, SelectedCard } from "./types.ts";
import type { HubConnection } from "@microsoft/signalr";

export type TabName = "table" | "hand" | "chat" | "info";

export interface AppState {
  playerId: string | null;
  roomCode: string | null;
  currentState: GameStateView | null;
  lastStateJson: string | null;
  selectedCard: SelectedCard | null;
  abilitySelectedIndices: number[];
  abilityTargetId: string | null;
  connection: HubConnection | null;
  statePollTimer: ReturnType<typeof setInterval> | null;
  activeTab: TabName;
  isMobile: boolean;
  isJoining: boolean;
}

export const state: AppState = {
  playerId: null,
  roomCode: null,
  currentState: null,
  lastStateJson: null,
  selectedCard: null,
  abilitySelectedIndices: [],
  abilityTargetId: null,
  connection: null,
  statePollTimer: null,
  activeTab: "table",
  isMobile: false,
  isJoining: false,
};
