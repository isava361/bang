import "./styles.css";
import { setUpdateStateHandler } from "./api.ts";
import { initSignalR } from "./signalr.ts";
import {
  renderLibrary, setGameHandlers, setJoinRoomHandler,
  updateState, renderRoomList, setStatus,
  showAbilityOverlay, hideAbilityOverlay, hideTargetOverlay,
} from "./ui.ts";
import {
  joinGame, startGame, endTurn, sendChat, newGame,
  leaveRoom, renamePlayer, createRoom, joinRoom,
  useAbility, tryReconnect, playCard, respondToAction,
  refreshRoomList, updateSettings, useGreenCard,
} from "./game.ts";
import {
  joinButton, startButton, endTurnButton, chatButton,
  cancelTarget, responsePass, newGameButton, leaveButton,
  renameButton, createRoomButton, joinRoomButton, roomCodeInput,
  abilityButton, cancelAbility, abilityConfirm,
  playerNameInput, chatInput, lobbyPanel,
} from "./dom.ts";
import { state } from "./state.ts";
import { initTabs } from "./tabs.ts";

// Wire circular-dependency-breaking callbacks
setUpdateStateHandler(updateState);
setGameHandlers({ playCard, respondToAction, updateSettings, useGreenCard });
setJoinRoomHandler(joinRoom);

// Event listeners
joinButton.addEventListener("click", joinGame);
startButton.addEventListener("click", startGame);
endTurnButton.addEventListener("click", endTurn);
chatButton.addEventListener("click", sendChat);
cancelTarget.addEventListener("click", hideTargetOverlay);
responsePass.addEventListener("click", () => respondToAction("pass", null));
newGameButton.addEventListener("click", newGame);
leaveButton.addEventListener("click", leaveRoom);
renameButton.addEventListener("click", renamePlayer);
createRoomButton.addEventListener("click", createRoom);

joinRoomButton.addEventListener("click", () => {
  const code = roomCodeInput.value.trim().toUpperCase();
  if (code) joinRoom(code);
  else setStatus("Введите код комнаты.");
});

abilityButton.addEventListener("click", showAbilityOverlay);
cancelAbility.addEventListener("click", hideAbilityOverlay);
abilityConfirm.addEventListener("click", useAbility);

playerNameInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") joinGame();
});

roomCodeInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    const code = roomCodeInput.value.trim().toUpperCase();
    if (code) joinRoom(code);
  }
});

chatInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") sendChat();
});

// Initialize
renderLibrary();
initTabs();

const onReconnected = async (): Promise<void> => {
  if (state.playerId && state.connection?.state === "Connected") {
    await state.connection.invoke("Register").catch(() => {});
  }
  if (state.roomCode && state.connection?.state === "Connected") {
    await state.connection.invoke("JoinRoom", state.roomCode).catch(() => {});
    try {
      const response = await fetch("/api/reconnect");
      const payload = await response.json();
      if (response.ok && payload.data) updateState(payload.data);
    } catch {
      // silent
    }
  } else if (!lobbyPanel.classList.contains("hidden")) {
    if (state.connection?.state === "Connected") {
      await state.connection.invoke("JoinRoom", "lobby").catch(() => {});
      refreshRoomList();
    }
  }
};

initSignalR(updateState, renderRoomList, onReconnected);
tryReconnect();
