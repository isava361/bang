import type { GameStateView } from "./types.ts";
import { apiPost, syncState, startStatePolling, stopStatePolling } from "./api.ts";
import { restartSignalR } from "./signalr.ts";
import { state } from "./state.ts";
import { joinPanel, lobbyPanel, gamePanel, playerNameInput, chatInput } from "./dom.ts";
import { setStatus, updateState, hideTargetOverlay, hideResponseOverlay, hideAbilityOverlay, renderRoomList } from "./ui.ts";

export const enterLobby = (): void => {
  joinPanel.classList.add("hidden");
  lobbyPanel.classList.remove("hidden");
  gamePanel.classList.add("hidden");
  refreshRoomList();
  if (state.connection && state.connection.state === "Connected") {
    state.connection.invoke("JoinRoom", "lobby").catch(() => {});
  }
};

export const joinGame = async (): Promise<void> => {
  const name = playerNameInput.value.trim();
  if (!name) {
    setStatus("Введите имя, чтобы присоединиться.");
    return;
  }
  localStorage.setItem("bangPlayerName", name);
  enterLobby();
};

export const createRoom = async (): Promise<void> => {
  if (state.isJoining) return;
  try {
    const data = await apiPost<{ roomCode: string }>("/api/room/create", {});
    const code = data.roomCode;
    await joinRoom(code);
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const joinRoom = async (code: string): Promise<void> => {
  if (state.isJoining) return;
  const name = localStorage.getItem("bangPlayerName") || playerNameInput.value.trim();
  if (!name) {
    setStatus("Сначала введите имя.");
    return;
  }
  state.isJoining = true;
  try {
    const stateView = await apiPost<GameStateView>("/api/join", { name, roomCode: code });
    state.playerId = stateView.yourPublicId || null;
    state.roomCode = stateView.roomCode || code;
    localStorage.setItem("bangPlayerName", name);
    lobbyPanel.classList.add("hidden");
    joinPanel.classList.add("hidden");
    gamePanel.classList.remove("hidden");
    setStatus(`Подключены как ${name}`);
    updateState(stateView);
    startStatePolling();
    if (state.connection) {
      await restartSignalR();
      if (state.connection.state === "Connected") {
        state.connection.invoke("Register").catch(() => {});
        state.connection.invoke("JoinRoom", state.roomCode || code).catch(() => {});
      }
    }
  } catch (error) {
    setStatus((error as Error).message);
  } finally {
    state.isJoining = false;
  }
};

export const leaveRoom = async (): Promise<void> => {
  if (!state.playerId) return;
  const oldRoom = state.roomCode;
  try {
    await apiPost("/api/leave", {});
  } catch {
    // silent
  }
  if (state.connection && state.connection.state === "Connected" && oldRoom) {
    state.connection.invoke("LeaveRoom", oldRoom).catch(() => {});
  }
  state.playerId = null;
  state.roomCode = null;
  state.currentState = null;
  state.lastStateJson = null;
  stopStatePolling();
  gamePanel.classList.add("hidden");
  enterLobby();
  setStatus("Вы вышли из комнаты.");
};

export const renamePlayer = async (): Promise<void> => {
  if (!state.playerId) return;
  const currentName = localStorage.getItem("bangPlayerName") || "";
  const newName = prompt("Введите новое имя:", currentName);
  if (!newName || !newName.trim() || newName.trim() === currentName) return;
  try {
    await apiPost("/api/rename", { newName: newName.trim() });
    localStorage.setItem("bangPlayerName", newName.trim());
    setStatus(`Имя изменено на ${newName.trim()}`);
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const refreshRoomList = async (): Promise<void> => {
  try {
    const response = await fetch("/api/rooms");
    const payload = await response.json();
    if (response.ok && payload.data) renderRoomList(payload.data);
  } catch {
    // silent
  }
};

export const updateSettings = async (dodgeCity: boolean, highNoon: boolean, fistfulOfCards: boolean): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/settings", { dodgeCity, highNoon, fistfulOfCards });
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const useGreenCard = async (cardIndex: number, targetId: string | null): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/usegreen", { cardIndex, targetId });
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const startGame = async (): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/start", {});
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const playCard = async (index: number, targetId: string | null): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/play", { cardIndex: index, targetId });
    hideTargetOverlay();
    await syncState();
  } catch (error) {
    hideTargetOverlay();
    setStatus((error as Error).message);
  }
};

export const endTurn = async (): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/end", {});
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const sendChat = async (): Promise<void> => {
  if (!state.playerId) return;
  const text = chatInput.value.trim();
  if (!text) return;
  try {
    await apiPost("/api/chat", { text });
    chatInput.value = "";
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const newGame = async (): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/newgame", {});
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const respondToAction = async (responseType: string, cardIndex: number | null, targetId?: string | null): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/respond", {
      responseType,
      cardIndex,
      targetId: targetId || null,
    });
    hideResponseOverlay();
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const useAbility = async (): Promise<void> => {
  if (!state.playerId) return;
  try {
    await apiPost("/api/ability", {
      cardIndices: state.abilitySelectedIndices,
      targetId: state.abilityTargetId || undefined,
    });
    hideAbilityOverlay();
    await syncState();
  } catch (error) {
    setStatus((error as Error).message);
  }
};

export const tryReconnect = async (): Promise<void> => {
  const savedName = localStorage.getItem("bangPlayerName");
  try {
    const response = await fetch("/api/reconnect");
    const payload = await response.json();
    if (response.ok && payload.data) {
      const stateView = payload.data as GameStateView;
      state.playerId = stateView.yourPublicId || null;
      state.roomCode = stateView.roomCode || null;
      joinPanel.classList.add("hidden");
      lobbyPanel.classList.add("hidden");
      gamePanel.classList.remove("hidden");
      setStatus(`Переподключены как ${savedName || "игрок"}`);
      updateState(stateView);
      startStatePolling();
      if (state.connection && state.connection.state === "Connected") {
        state.connection.invoke("Register").catch(() => {});
        if (state.roomCode) state.connection.invoke("JoinRoom", state.roomCode).catch(() => {});
      }
    } else if (savedName) {
      enterLobby();
    }
  } catch {
    if (savedName) enterLobby();
  }
};
