import * as signalR from "@microsoft/signalr";
import type { GameStateView, RoomInfo } from "./types.ts";
import { state } from "./state.ts";
import { lobbyPanel } from "./dom.ts";

export const initSignalR = async (
  onStateUpdated: (s: GameStateView) => void,
  onRoomsUpdated: (rooms: RoomInfo[]) => void,
  onReconnected: () => Promise<void>,
): Promise<void> => {
  state.connection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .withAutomaticReconnect()
    .build();

  state.connection.on("StateUpdated", (s: GameStateView) => {
    onStateUpdated(s);
  });

  state.connection.on("RoomsUpdated", (rooms: RoomInfo[]) => {
    onRoomsUpdated(rooms);
  });

  state.connection.onreconnected(async () => {
    await onReconnected();
  });

  try {
    await state.connection.start();
    if (state.connection.state === "Connected") {
      if (state.playerId) {
        await state.connection.invoke("Register").catch(() => {});
        if (state.roomCode) await state.connection.invoke("JoinRoom", state.roomCode).catch(() => {});
      } else if (!lobbyPanel.classList.contains("hidden")) {
        await state.connection.invoke("JoinRoom", "lobby").catch(() => {});
      }
    }
  } catch (err) {
    console.error("SignalR connection failed:", err);
  }
};

export const restartSignalR = async (): Promise<void> => {
  if (!state.connection) return;
  try {
    const s = state.connection.state;
    if (s === "Connected" || s === "Connecting" || s === "Reconnecting") {
      await state.connection.stop();
    }
    await state.connection.start();
  } catch (err) {
    console.error("SignalR restart failed:", err);
  }
};
