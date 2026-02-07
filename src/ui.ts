import type { CardView, GameStateView, PendingActionView, PlayerView, RoomInfo } from "./types.ts";
import { suitColors, cardTypeLabels, cardCategoryLabels, cardsReference, charactersReference, rolesReference, roleDistribution } from "./constants.ts";
import { escapeHtml, formatCountLabel, formatSuitValue, computeTablePositions } from "./utils.ts";
import {
  connectionStatus, spectatorBanner, roomCodeBadge, turnInfo,
  eventLog, chatLog, newGameButton, playersContainer, handCards,
  handHint, endTurnButton, startButton, abilityButton,
  targetOverlay, targetList, targetPrompt, responseOverlay,
  responseTitle, responsePrompt, responseCards, responsePass,
  abilityOverlay, abilityCards, abilityConfirm,
  cardLibrary, characterLibrary, roleLibrary, roomListContainer,
  settingsPanel, settDodgeCity, settHighNoon, settFistful,
  eventBanner,
} from "./dom.ts";
import { state } from "./state.ts";
import { updateMobileState } from "./tabs.ts";

const ROLE_SHERIFF = "\u0428\u0435\u0440\u0438\u0444";
const ROLE_DEPUTY = "\u041F\u043E\u043C\u043E\u0449\u043D\u0438\u043A";
const ROLE_OUTLAW = "\u0411\u0430\u043D\u0434\u0438\u0442";
const ROLE_RENEGADE = "\u0420\u0435\u043D\u0435\u0433\u0430\u0442";

const CHAR_JANET = "\u041A\u0430\u043B\u0430\u043C\u0438\u0442\u0438 \u0414\u0436\u0430\u043D\u0435\u0442";
const CHAR_ELENA = "\u0415\u043B\u0435\u043D\u0430 \u0424\u0443\u044D\u043D\u0442\u0435";
const CHAR_CHUCK = "\u0427\u0430\u043A \u0412\u0435\u043D\u0433\u0430\u043C";
const CHAR_JOSE = "\u0425\u043E\u0441\u0435 \u0414\u0435\u043B\u044C\u0433\u0430\u0434\u043E";
const CHAR_DOC = "\u0414\u043E\u043A \u0425\u043E\u043B\u0438\u0434\u044D\u0439";
const CHAR_SID = "\u0421\u0438\u0434 \u041A\u0435\u0442\u0447\u0443\u043C";
const DOCTOR_EVENT = "\u0414\u043E\u043A\u0442\u043E\u0440";

// Injected game action callbacks (set from main.ts to break circular dependency)
let playCardFn: ((index: number, targetId: string | null) => Promise<void>) | null = null;
let respondToActionFn: ((responseType: string, cardIndex: number | null, targetId?: string | null) => Promise<void>) | null = null;
let updateSettingsFn: ((dodgeCity: boolean, highNoon: boolean, fistfulOfCards: boolean) => Promise<void>) | null = null;
let useGreenCardFn: ((cardIndex: number, targetId: string | null) => Promise<void>) | null = null;

export function setGameHandlers(handlers: {
  playCard: (index: number, targetId: string | null) => Promise<void>;
  respondToAction: (responseType: string, cardIndex: number | null, targetId?: string | null) => Promise<void>;
  updateSettings: (dodgeCity: boolean, highNoon: boolean, fistfulOfCards: boolean) => Promise<void>;
  useGreenCard: (cardIndex: number, targetId: string | null) => Promise<void>;
}): void {
  playCardFn = handlers.playCard;
  respondToActionFn = handlers.respondToAction;
  updateSettingsFn = handlers.updateSettings;
  useGreenCardFn = handlers.useGreenCard;
}

export const setStatus = (text: string): void => {
  connectionStatus.textContent = text;
};

// Wire settings checkbox change events
const onSettingsChange = (): void => {
  updateSettingsFn?.(settDodgeCity.checked, settHighNoon.checked, settFistful.checked);
};
settDodgeCity.addEventListener("change", onSettingsChange);
settHighNoon.addEventListener("change", onSettingsChange);
settFistful.addEventListener("change", onSettingsChange);

const getMyCharacterName = (s: GameStateView): string => {
  const me = s.players.find((p) => p.id === s.yourPublicId);
  return me ? me.characterName : "";
};

const getCardTypeClass = (type: string): string => {
  const map: Record<string, string> = {
    Bang: "attack", Gatling: "attack", Indians: "attack", Duel: "attack",
    Punch: "attack", Springfield: "attack", Howitzer: "attack",
    Pepperbox: "attack", BuffaloRifle: "attack", Derringer: "attack",
    Missed: "defense", Barrel: "defense", Dodge: "defense",
    Bible: "defense", IronPlate: "defense", Sombrero: "defense", TenGallonHat: "defense",
    Volcanic: "weapon", Schofield: "weapon", Remington: "weapon", RevCarabine: "weapon", Winchester: "weapon",
    Mustang: "equipment", Scope: "equipment", Jail: "equipment", Dynamite: "equipment",
    Hideout: "equipment", Silver: "equipment",
  };
  return map[type] || "utility";
};

const getRoleClass = (role: string): string => {
  if (role.includes(ROLE_SHERIFF)) return "role--sheriff";
  if (role.includes(ROLE_DEPUTY)) return "role--deputy";
  if (role.includes(ROLE_OUTLAW)) return "role--outlaw";
  if (role.includes(ROLE_RENEGADE)) return "role--renegade";
  return "role--hidden";
};

const createPlayerCard = (player: PlayerView, index: number, stateView: GameStateView): HTMLDivElement => {
  const card = document.createElement("div");
  card.className = "player-card";
  if (player.id === stateView.currentPlayerId) card.classList.add("active");
  if (!player.isAlive) card.classList.add("out");
  if (index === 0 && !stateView.isSpectator) card.classList.add("self");

  const portraitHtml = player.characterPortrait
    ? `<img class="player-portrait" src="${escapeHtml(player.characterPortrait)}" alt="${escapeHtml(player.characterName)}" loading="lazy" onerror="this.style.display='none'"/>`
    : "";

  const equipHtml = player.equipment && player.equipment.length > 0
    ? player.equipment.map((e) => {
        const sv = e.suit ? ` ${formatSuitValue(e)}` : "";
        const fresh = e.isFresh ? " \u23F3" : "";
        return `<span class="equip-tag">${escapeHtml(e.name)}${fresh}${sv}</span>`;
      }).join(" ")
    : "";

  const distanceHtml = stateView.distances && stateView.distances[player.id] != null
    ? `<small class="distance-label">\u0414\u0438\u0441\u0442: ${stateView.distances[player.id]}</small>`
    : "";

  const hostBadge = stateView.hostId === player.id ? '<span class="host-badge">\u0412\u0435\u0434\u0443\u0449\u0438\u0439</span>' : "";

  const roleClass = getRoleClass(player.role);
  const hpPercent = player.maxHp > 0 ? Math.round((player.hp / player.maxHp) * 100) : 0;

  card.innerHTML = `
    <div class="player-header">
      <div class="portrait-wrap">
        ${portraitHtml}
        <div class="portrait-ring ${roleClass}"></div>
      </div>
      <div class="player-info">
        <div class="player-name-row">
          <strong>${escapeHtml(player.name)}</strong>
          ${hostBadge}
        </div>
        <span class="player-char">${escapeHtml(player.characterName)}</span>
      </div>
    </div>
    <div class="hp-bar-wrap">
      <div class="hp-bar" style="width:${hpPercent}%"></div>
      <span class="hp-text">${player.hp}/${player.maxHp}</span>
    </div>
    ${player.role !== "?" ? `<span class="role-badge ${roleClass}">${escapeHtml(player.role)}</span>` : ""}
    <small class="player-desc">${escapeHtml(player.characterDescription)}</small>
    <div class="player-meta">
      <span>\u041A\u0430\u0440\u0442: ${player.handCount}</span>
      ${distanceHtml}
    </div>
    ${equipHtml ? `<div class="equip-row">${equipHtml}</div>` : ""}
  `;
  return card;
};

export const renderLibrary = (): void => {
  cardLibrary.innerHTML = "";
  cardsReference.forEach((card) => {
    const item = document.createElement("div");
    item.className = "library-item";
    item.innerHTML = `
      <div class="library-row">
        <img class="library-image" src="${card.imagePath}" alt="${card.name}" loading="lazy" onerror="this.style.display='none'"/>
        <div>
          <strong>${card.name}</strong>
          <p>${card.description}</p>
        </div>
      </div>
    `;
    cardLibrary.appendChild(item);
  });

  characterLibrary.innerHTML = "";
  charactersReference.forEach((character) => {
    const item = document.createElement("div");
    item.className = "library-item";
    item.innerHTML = `
      <div class="library-row">
        <img class="library-image" src="${character.portraitPath}" alt="${character.name}" loading="lazy" onerror="this.style.display='none'"/>
        <div>
          <strong>${character.name}</strong>
          <p>${character.description}</p>
        </div>
      </div>
    `;
    characterLibrary.appendChild(item);
  });

  roleLibrary.innerHTML = "";
  rolesReference.forEach((role) => {
    const item = document.createElement("div");
    item.className = "library-item";
    item.innerHTML = `
      <div>
        <strong style="color:${role.color}">${role.name}</strong>
        <p>${role.description}</p>
      </div>
    `;
    roleLibrary.appendChild(item);
  });

  const distItem = document.createElement("div");
  distItem.className = "library-item";
  distItem.innerHTML = `
    <div>
      <strong>\u0420\u0430\u0441\u043F\u0440\u0435\u0434\u0435\u043B\u0435\u043D\u0438\u0435 \u0440\u043E\u043B\u0435\u0439</strong>
      <table class="role-table">
        ${roleDistribution.map((r) => `<tr><td>${r.players} \u0438\u0433\u0440.</td><td>${r.roles}</td></tr>`).join("")}
      </table>
    </div>
  `;
  roleLibrary.appendChild(distItem);
};

export const showTargetOverlay = (card: CardView, index: number): void => {
  if (!state.currentState) return;

  state.selectedCard = { card, index };
  targetList.innerHTML = "";
  targetPrompt.textContent = card.targetHint || "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0446\u0435\u043B\u044C.";

  const effectiveType = card.type === "Missed" && getMyCharacterName(state.currentState) === CHAR_JANET
    ? "Bang"
    : card.type;

  const includeSelf = card.type === "Tequila";
  const availableTargets = state.currentState.players
    .filter((player) => (includeSelf || player.id !== state.currentState!.yourPublicId) && player.isAlive)
    .map((player) => {
      const dist = state.currentState!.distances ? state.currentState!.distances[player.id] : null;
      let outOfRange = false;
      if (effectiveType === "Bang" && dist != null && dist > state.currentState!.weaponRange) outOfRange = true;
      if (effectiveType === "Panic" && dist != null && dist > 1) outOfRange = true;
      if (card.type === "Punch" && dist != null && dist > 1) outOfRange = true;
      return { ...player, distance: dist, outOfRange };
    });

  if (availableTargets.length === 0) {
    targetPrompt.textContent = "\u041F\u043E\u043A\u0430 \u043D\u0435\u0442 \u0434\u043E\u0441\u0442\u0443\u043F\u043D\u044B\u0445 \u0446\u0435\u043B\u0435\u0439.";
    const empty = document.createElement("div");
    empty.className = "hint";
    empty.textContent = "\u0426\u0435\u043B\u0438 \u043F\u043E\u044F\u0432\u044F\u0442\u0441\u044F, \u043A\u043E\u0433\u0434\u0430 \u043A \u0441\u0442\u043E\u043B\u0443 \u043F\u0440\u0438\u0441\u043E\u0435\u0434\u0438\u043D\u0438\u0442\u0441\u044F \u0434\u0440\u0443\u0433\u043E\u0439 \u0438\u0433\u0440\u043E\u043A.";
    targetList.appendChild(empty);
  } else {
    availableTargets.forEach((player) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const distLabel = player.distance != null ? ` [\u0434\u0438\u0441\u0442: ${player.distance}]` : "";
      button.textContent = `${player.name} (${player.characterName})${distLabel}`;
      if (player.outOfRange) {
        button.disabled = true;
        button.title = "\u0412\u043D\u0435 \u0434\u0430\u043B\u044C\u043D\u043E\u0441\u0442\u0438";
      } else {
        button.addEventListener("click", () => playCardFn?.(index, player.id));
      }
      targetList.appendChild(button);
    });
  }

  targetOverlay.classList.remove("hidden");
};

export const hideTargetOverlay = (): void => {
  state.selectedCard = null;
  targetOverlay.classList.add("hidden");
};

const reactiveGreenTypes = new Set(["Bible", "IronPlate", "Sombrero", "TenGallonHat"]);
const activeGreenTargeted = new Set(["Derringer", "Pepperbox", "BuffaloRifle"]);
const activeGreenUntargeted = new Set(["Howitzer", "Canteen"]);

export const showGreenCardTargetOverlay = (greenIdx: number, greenCard: CardView): void => {
  if (!state.currentState) return;
  state.selectedCard = null;
  targetList.innerHTML = "";
  targetPrompt.textContent = greenCard.targetHint || "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0446\u0435\u043B\u044C.";

  const maxRange = greenCard.type === "Derringer" ? 1 : Infinity;
  const targets = state.currentState.players
    .filter((p) => p.id !== state.currentState!.yourPublicId && p.isAlive)
    .map((p) => {
      const dist = state.currentState!.distances?.[p.id] ?? null;
      const outOfRange = dist != null && dist > maxRange;
      return { ...p, distance: dist, outOfRange };
    });

  targets.forEach((player) => {
    const button = document.createElement("button");
    button.className = "target-button";
    const distLabel = player.distance != null ? ` [\u0434\u0438\u0441\u0442: ${player.distance}]` : "";
    button.textContent = `${player.name} (${player.characterName})${distLabel}`;
    if (player.outOfRange) {
      button.disabled = true;
      button.title = "\u0412\u043D\u0435 \u0434\u0430\u043B\u044C\u043D\u043E\u0441\u0442\u0438";
    } else {
      button.addEventListener("click", () => {
        useGreenCardFn?.(greenIdx, player.id);
        hideTargetOverlay();
      });
    }
    targetList.appendChild(button);
  });

  targetOverlay.classList.remove("hidden");
};

export const showResponseOverlay = (pendingAction: PendingActionView, s: GameStateView): void => {
  responseCards.innerHTML = "";
  responsePrompt.textContent = pendingAction.message;

  const type = pendingAction.type;

  if (type === "GeneralStorePick" || type === "KitCarlsonPick") {
    responseTitle.textContent = type === "GeneralStorePick" ? "\u041C\u0430\u0433\u0430\u0437\u0438\u043D" : "\u041A\u0438\u0442 \u041A\u0430\u0440\u043B\u0441\u043E\u043D";
    responsePass.classList.add("hidden");
    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "DiscardExcess" || type === "DiscardForCost") {
    responseTitle.textContent = type === "DiscardExcess" ? "\u0421\u0431\u0440\u043E\u0441" : "\u0421\u0431\u0440\u043E\u0441 \u0434\u043B\u044F \u044D\u0444\u0444\u0435\u043A\u0442\u0430";
    responsePass.classList.add("hidden");
    s.yourHand.forEach((card, idx) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
      button.textContent = `${card.name}${sv}`;
      button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
      responseCards.appendChild(button);
    });
  } else if (type === "BrawlDefense") {
    responseTitle.textContent = "\u041F\u043E\u0442\u0430\u0441\u043E\u0432\u043A\u0430";
    responsePass.classList.add("hidden");
    s.yourHand.forEach((card, idx) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
      button.textContent = `\u0420\u0443\u043A\u0430: ${card.name}${sv}`;
      button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
      responseCards.appendChild(button);
    });
    const me = s.players.find((p) => p.id === s.yourPublicId);
    if (me) {
      me.equipment.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `\u0421\u043D\u0430\u0440\u044F\u0436\u0435\u043D\u0438\u0435: ${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("equipment", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "ChooseStealSource") {
    responseTitle.textContent = "\u0412\u044B\u0431\u043E\u0440 \u0446\u0435\u043B\u0438";
    responsePass.classList.add("hidden");

    const handButton = document.createElement("button");
    handButton.className = "target-button";
    handButton.textContent = "\u0421\u043B\u0443\u0447\u0430\u0439\u043D\u0430\u044F \u043A\u0430\u0440\u0442\u0430 \u0438\u0437 \u0440\u0443\u043A\u0438";
    handButton.addEventListener("click", () => respondToActionFn?.("hand", null));
    responseCards.appendChild(handButton);

    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `\u0421\u043D\u0430\u0440\u044F\u0436\u0435\u043D\u0438\u0435: ${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("equipment", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "JesseJonesSteal") {
    responseTitle.textContent = "\u0414\u0436\u0435\u0441\u0441\u0438 \u0414\u0436\u043E\u043D\u0441";
    responsePass.classList.add("hidden");

    const targets = s.players.filter(
      (p) => p.id !== s.yourPublicId && p.isAlive && p.handCount > 0,
    );
    if (targets.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "\u041D\u0435\u0442 \u0438\u0433\u0440\u043E\u043A\u043E\u0432 \u0441 \u043A\u0430\u0440\u0442\u0430\u043C\u0438 \u0434\u043B\u044F \u0432\u0437\u044F\u0442\u0438\u044F.";
      responseCards.appendChild(hint);
    } else {
      targets.forEach((player) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `${player.name} (${player.handCount} \u043A\u0430\u0440\u0442)`;
        button.addEventListener("click", () => respondToActionFn?.("steal", null, player.id));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "VeraCusterCopy") {
    responseTitle.textContent = "\u0412\u0435\u0440\u0430 \u041A\u0430\u0441\u0442\u0435\u0440";
    responsePass.classList.add("hidden");

    const copyTargets = s.players.filter(
      (p) => p.id !== s.yourPublicId && p.isAlive,
    );
    copyTargets.forEach((player) => {
      const button = document.createElement("button");
      button.className = "target-button";
      button.textContent = `${player.characterName} (${player.name})`;
      button.addEventListener("click", () => respondToActionFn?.("copy", null, player.id));
      responseCards.appendChild(button);
    });
  } else if (type === "PatBrennanDraw") {
    responseTitle.textContent = "\u041F\u0430\u0442 \u0411\u0440\u0435\u043D\u043D\u0430\u043D";
    responsePass.classList.add("hidden");

    const deckButton = document.createElement("button");
    deckButton.className = "target-button";
    deckButton.textContent = "\u0414\u043E\u0431\u0440\u0430\u0442\u044C 2 \u0438\u0437 \u043A\u043E\u043B\u043E\u0434\u044B";
    deckButton.addEventListener("click", () => respondToActionFn?.("draw_deck", null));
    responseCards.appendChild(deckButton);

    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `\u0417\u0430\u0431\u0440\u0430\u0442\u044C: ${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("equipment", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "RussianRoulette") {
    responseTitle.textContent = "\u0420\u0443\u0441\u0441\u043A\u0430\u044F \u0440\u0443\u043B\u0435\u0442\u043A\u0430";
    responsePass.classList.remove("hidden");
    responsePass.textContent = "\u041F\u043E\u0442\u0435\u0440\u044F\u0442\u044C 1 \u041E\u0417";

    const myChar = getMyCharacterName(s);
    const isJanet = myChar === CHAR_JANET;
    const bangCards = s.yourHand
      .map((card, idx) => ({ card, idx }))
      .filter(({ card }) => card.type === "Bang" || (isJanet && card.type === "Missed"));

    if (bangCards.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "\u0423 \u0432\u0430\u0441 \u043D\u0435\u0442 \u0411\u044D\u043D\u0433! \u0434\u043B\u044F \u0441\u0431\u0440\u043E\u0441\u0430.";
      responseCards.appendChild(hint);
    } else {
      bangCards.forEach(({ card, idx }) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `\u0421\u0431\u0440\u043E\u0441\u0438\u0442\u044C ${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "TrainRobbery") {
    responseTitle.textContent = "\u041E\u0433\u0440\u0430\u0431\u043B\u0435\u043D\u0438\u0435 \u043F\u043E\u0435\u0437\u0434\u0430";
    responsePass.classList.remove("hidden");
    responsePass.textContent = "\u041F\u043E\u0442\u0435\u0440\u044F\u0442\u044C 1 \u041E\u0417";

    if (s.yourHand.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "\u0423 \u0432\u0430\u0441 \u043D\u0435\u0442 \u043A\u0430\u0440\u0442 \u0434\u043B\u044F \u043F\u0435\u0440\u0435\u0434\u0430\u0447\u0438.";
      responseCards.appendChild(hint);
    } else {
      s.yourHand.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `\u041F\u0435\u0440\u0435\u0434\u0430\u0442\u044C ${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (
    type === "BangDefense" ||
    type === "GatlingDefense" ||
    type === "HowitzerDefense" ||
    type === "IndiansDefense" ||
    type === "DuelChallenge"
  ) {
    const isIndians = type === "IndiansDefense";
    const isDuel = type === "DuelChallenge";
    const isDefense = type === "BangDefense" || type === "GatlingDefense" || type === "HowitzerDefense";
    const myChar = getMyCharacterName(s);
    const isJanet = myChar === CHAR_JANET;
    const isElena = myChar === CHAR_ELENA;

    let requiredTypes: string[];
    if (isElena && isDefense) {
      requiredTypes = [];
    } else if (isIndians || isDuel) {
      requiredTypes = isJanet ? ["Bang", "Missed"] : ["Bang"];
    } else {
      requiredTypes = isJanet ? ["Missed", "Dodge", "Bang"] : ["Missed", "Dodge"];
    }
    const requiredName = (isElena && isDefense) ? "\u043B\u044E\u0431\u0443\u044E \u043A\u0430\u0440\u0442\u0443" : requiredTypes.map((t) => {
      if (t === "Bang") return "\u0411\u044D\u043D\u0433!";
      if (t === "Dodge") return "\u0423\u0432\u043E\u0440\u043E\u0442";
      return "\u041C\u0438\u043C\u043E!";
    }).join("/");

    responseTitle.textContent = isDuel ? "\u0414\u0443\u044D\u043B\u044C" : "\u0417\u0430\u0449\u0438\u0442\u0430";
    responsePass.classList.remove("hidden");
    responsePass.textContent = isDuel ? "\u0421\u0434\u0430\u0442\u044C\u0441\u044F" : "\u041F\u0440\u0438\u043D\u044F\u0442\u044C \u0443\u0434\u0430\u0440";

    const validCards = s.yourHand
      .map((card, idx) => ({ card, idx }))
      .filter(({ card }) => (isElena && isDefense) || requiredTypes.includes(card.type));

    if (validCards.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = `\u0423 \u0432\u0430\u0441 \u043D\u0435\u0442 \u043A\u0430\u0440\u0442 ${requiredName} \u0434\u043B\u044F \u043E\u0442\u0432\u0435\u0442\u0430.`;
      responseCards.appendChild(hint);
    } else {
      validCards.forEach(({ card, idx }) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `\u0421\u044B\u0433\u0440\u0430\u0442\u044C ${card.name}`;
        button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
        responseCards.appendChild(button);
      });
    }

    if (isDefense) {
      const me = s.players.find((p) => p.id === s.yourPublicId);
      if (me) {
        me.equipment
          .map((card, idx) => ({ card, idx }))
          .filter(({ card }) => reactiveGreenTypes.has(card.type) && !card.isFresh)
          .forEach(({ card, idx }) => {
            const button = document.createElement("button");
            button.className = "target-button";
            button.textContent = `\u0421\u043D\u0430\u0440\u044F\u0436\u0435\u043D\u0438\u0435: ${card.name}`;
            button.addEventListener("click", () => respondToActionFn?.("play_green", idx));
            responseCards.appendChild(button);
          });
      }
    }
  } else {
    // Safe fallback for unknown pending action types.
    responseTitle.textContent = "\u0414\u0435\u0439\u0441\u0442\u0432\u0438\u0435";
    responsePass.classList.add("hidden");
    if (s.yourHand.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "\u041D\u0435\u0442 \u043A\u0430\u0440\u0442 \u0434\u043B\u044F \u043E\u0442\u0432\u0435\u0442\u0430.";
      responseCards.appendChild(hint);
    } else {
      s.yourHand.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `${card.name}${sv}`;
        button.addEventListener("click", () => respondToActionFn?.("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  }

  responseOverlay.classList.remove("hidden");
};

export const hideResponseOverlay = (): void => {
  responseOverlay.classList.add("hidden");
};

const showCardTooltip = (element: HTMLElement, message: string): void => {
  const tooltip = document.createElement("div");
  tooltip.className = "card-tooltip";
  tooltip.textContent = message;
  element.appendChild(tooltip);

  setTimeout(() => {
    tooltip.remove();
  }, 2000);
};

export const onCardSelected = (card: CardView, index: number, element: HTMLElement): void => {
  if (!state.currentState || !state.playerId) return;

  if (!state.currentState.started) {
    setStatus("\u041D\u0430\u0447\u043D\u0438\u0442\u0435 \u0438\u0433\u0440\u0443 \u043F\u0435\u0440\u0435\u0434 \u0442\u0435\u043C, \u043A\u0430\u043A \u0438\u0433\u0440\u0430\u0442\u044C \u043A\u0430\u0440\u0442\u044B.");
    return;
  }

  if (state.currentState.pendingAction) {
    setStatus("\u041E\u0436\u0438\u0434\u0430\u043D\u0438\u0435 \u043E\u0442\u0432\u0435\u0442\u0430 \u0438\u0433\u0440\u043E\u043A\u0430.");
    return;
  }

  if (state.currentState.currentPlayerId !== state.playerId) {
    setStatus("\u0414\u043E\u0436\u0434\u0438\u0442\u0435\u0441\u044C \u0441\u0432\u043E\u0435\u0433\u043E \u0445\u043E\u0434\u0430, \u0447\u0442\u043E\u0431\u044B \u0441\u044B\u0433\u0440\u0430\u0442\u044C \u043A\u0430\u0440\u0442\u0443.");
    return;
  }

  const myChar = getMyCharacterName(state.currentState);
  const effectiveType = card.type === "Missed" && myChar === CHAR_JANET ? "Bang" : card.type;

  if (effectiveType === "Bang" && state.currentState.bangsPlayedThisTurn >= state.currentState.bangLimit) {
    showCardTooltip(element, `\u041C\u043E\u0436\u043D\u043E \u0441\u044B\u0433\u0440\u0430\u0442\u044C \u0442\u043E\u043B\u044C\u043A\u043E ${state.currentState.bangLimit} \u0411\u044D\u043D\u0433! \u0437\u0430 \u0445\u043E\u0434.`);
    return;
  }

  const needsTarget = card.requiresTarget || effectiveType === "Bang";
  if (needsTarget) {
    showTargetOverlay(card, index);
  } else {
    playCardFn?.(index, null);
  }
};

export const showAbilityOverlay = (): void => {
  if (!state.currentState) return;
  const myChar = getMyCharacterName(state.currentState);
  const isDoctorEvent = state.currentState.currentEventName === DOCTOR_EVENT;

  state.abilitySelectedIndices = [];
  state.abilityTargetId = null;
  abilityCards.innerHTML = "";
  abilityConfirm.disabled = true;

  const updateConfirmState = (): void => {
    if (isDoctorEvent) {
      abilityConfirm.disabled = state.abilitySelectedIndices.length !== 2;
    } else if (myChar === CHAR_CHUCK) {
      abilityConfirm.disabled = false;
    } else if (myChar === CHAR_JOSE) {
      abilityConfirm.disabled = state.abilitySelectedIndices.length !== 1;
    } else if (myChar === CHAR_DOC) {
      abilityConfirm.disabled = state.abilitySelectedIndices.length !== 2 || !state.abilityTargetId;
    } else {
      abilityConfirm.disabled = state.abilitySelectedIndices.length !== 2;
    }
  };

  if (isDoctorEvent) {
    const hint = document.createElement("div");
    hint.className = "hint";
    hint.textContent = "\u0421\u0431\u0440\u043E\u0441\u044C\u0442\u0435 2 \u043A\u0430\u0440\u0442\u044B \u0438 \u0432\u043E\u0441\u0441\u0442\u0430\u043D\u043E\u0432\u0438\u0442\u0435 1 \u041E\u0417.";
    abilityCards.appendChild(hint);

    state.currentState.yourHand.forEach((card, idx) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
      button.textContent = `${card.name}${sv}`;
      button.addEventListener("click", () => {
        if (state.abilitySelectedIndices.includes(idx)) {
          state.abilitySelectedIndices = state.abilitySelectedIndices.filter((i) => i !== idx);
          button.classList.remove("selected");
        } else if (state.abilitySelectedIndices.length < 2) {
          state.abilitySelectedIndices.push(idx);
          button.classList.add("selected");
        }
        updateConfirmState();
      });
      abilityCards.appendChild(button);
    });
  } else if (myChar === CHAR_CHUCK) {
    const hint = document.createElement("div");
    hint.className = "hint";
    hint.textContent = "\u041F\u043E\u0442\u0435\u0440\u044F\u0439\u0442\u0435 1 \u041E\u0417 \u0438 \u0434\u043E\u0431\u0435\u0440\u0438\u0442\u0435 2 \u043A\u0430\u0440\u0442\u044B.";
    abilityCards.appendChild(hint);
  } else {
    const cardFilter = myChar === CHAR_JOSE
      ? (c: CardView) => c.category === "Blue"
      : () => true;
    const maxSelect = myChar === CHAR_JOSE ? 1 : 2;

    state.currentState.yourHand.forEach((card, idx) => {
      if (!cardFilter(card)) return;
      const button = document.createElement("button");
      button.className = "target-button";
      const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
      button.textContent = `${card.name}${sv}`;
      button.addEventListener("click", () => {
        if (state.abilitySelectedIndices.includes(idx)) {
          state.abilitySelectedIndices = state.abilitySelectedIndices.filter((i) => i !== idx);
          button.classList.remove("selected");
        } else if (state.abilitySelectedIndices.length < maxSelect) {
          state.abilitySelectedIndices.push(idx);
          button.classList.add("selected");
        }
        updateConfirmState();
      });
      abilityCards.appendChild(button);
    });
  }

  if (!isDoctorEvent && myChar === CHAR_DOC) {
    const sep = document.createElement("div");
    sep.className = "hint";
    sep.textContent = "\u0412\u044B\u0431\u0435\u0440\u0438\u0442\u0435 \u0446\u0435\u043B\u044C:";
    abilityCards.appendChild(sep);

    const targets = state.currentState.players.filter(
      (p) => p.id !== state.currentState!.yourPublicId && p.isAlive,
    );
    targets.forEach((player) => {
      const button = document.createElement("button");
      button.className = "target-button";
      button.textContent = player.name;
      button.addEventListener("click", () => {
        abilityCards.querySelectorAll(".target-button.target-selected").forEach((b) => b.classList.remove("target-selected"));
        button.classList.add("target-selected");
        state.abilityTargetId = player.id;
        updateConfirmState();
      });
      abilityCards.appendChild(button);
    });
  }

  updateConfirmState();
  abilityOverlay.classList.remove("hidden");
};

export const hideAbilityOverlay = (): void => {
  abilityOverlay.classList.add("hidden");
  state.abilitySelectedIndices = [];
  state.abilityTargetId = null;
};

export const renderRoomList = (rooms: RoomInfo[]): void => {
  roomListContainer.innerHTML = "";
  if (!rooms || rooms.length === 0) {
    roomListContainer.innerHTML = '<p class="hint">\u041F\u043E\u043A\u0430 \u043D\u0435\u0442 \u043A\u043E\u043C\u043D\u0430\u0442. \u0421\u043E\u0437\u0434\u0430\u0439\u0442\u0435!</p>';
    return;
  }
  rooms.forEach((room) => {
    const item = document.createElement("div");
    item.className = "room-item";
    item.innerHTML = `
      <div>
        <strong class="room-code-badge">${escapeHtml(room.roomCode)}</strong>
        <span>${escapeHtml(room.statusText)}</span>
        <small>
          ${room.playerCount} ${formatCountLabel(room.playerCount, "\u0438\u0433\u0440\u043E\u043A", "\u0438\u0433\u0440\u043E\u043A\u0430", "\u0438\u0433\u0440\u043E\u043A\u043E\u0432")},
          ${room.spectatorCount} ${formatCountLabel(room.spectatorCount, "\u0437\u0440\u0438\u0442\u0435\u043B\u044C", "\u0437\u0440\u0438\u0442\u0435\u043B\u044F", "\u0437\u0440\u0438\u0442\u0435\u043B\u0435\u0439")}
        </small>
      </div>
      <button class="primary">\u0412\u043E\u0439\u0442\u0438</button>
    `;
    const btn = item.querySelector("button");
    if (btn) {
      btn.addEventListener("click", () => joinRoomFn?.(room.roomCode));
    }
    roomListContainer.appendChild(item);
  });
};

let joinRoomFn: ((code: string) => Promise<void>) | null = null;

export function setJoinRoomHandler(fn: (code: string) => Promise<void>): void {
  joinRoomFn = fn;
}

export const updateState = (stateView: GameStateView): void => {
  if (!stateView) return;

  const stateJson = JSON.stringify(stateView);
  if (stateJson === state.lastStateJson) return;
  state.lastStateJson = stateJson;

  state.currentState = stateView;
  state.playerId = stateView.yourPublicId || state.playerId;
  state.roomCode = stateView.roomCode || state.roomCode;
  const isSpectator = !!stateView.isSpectator;

  spectatorBanner.classList.toggle("hidden", !isSpectator);

  const isHost = stateView.hostId === state.playerId;
  const showSettings = !stateView.started && !isSpectator && isHost;
  settingsPanel.classList.toggle("hidden", !showSettings);
  if (stateView.settings) {
    settDodgeCity.checked = stateView.settings.dodgeCity;
    settHighNoon.checked = stateView.settings.highNoon;
    settFistful.checked = stateView.settings.fistfulOfCards;
  }
  settDodgeCity.disabled = !showSettings;
  settHighNoon.disabled = !showSettings;
  settFistful.disabled = !showSettings;

  if (stateView.currentEventName) {
    eventBanner.classList.remove("hidden");
    eventBanner.innerHTML = `<strong>${escapeHtml(stateView.currentEventName)}</strong> \u2014 ${escapeHtml(stateView.currentEventDescription || "")}`;
  } else {
    eventBanner.classList.add("hidden");
    eventBanner.innerHTML = "";
  }

  if (stateView.roomCode) {
    roomCodeBadge.textContent = `\u041A\u043E\u043C\u043D\u0430\u0442\u0430: ${stateView.roomCode}`;
    roomCodeBadge.classList.remove("hidden");
  }

  if (stateView.gameOver) {
    turnInfo.textContent = stateView.winnerMessage || "\u0418\u0433\u0440\u0430 \u043E\u043A\u043E\u043D\u0447\u0435\u043D\u0430.";
  } else {
    turnInfo.textContent = stateView.started
      ? `\u0425\u043E\u0434: ${stateView.currentPlayerName}`
      : "\u041E\u0436\u0438\u0434\u0430\u043D\u0438\u0435 \u043D\u0430\u0447\u0430\u043B\u0430 \u0438\u0433\u0440\u044B...";
  }

  eventLog.innerHTML = "";
  if (stateView.eventLog && stateView.eventLog.length > 0) {
    stateView.eventLog.forEach((evt) => {
      const p = document.createElement("p");
      p.className = "event-entry";
      p.textContent = evt;
      eventLog.appendChild(p);
    });
  } else {
    eventLog.innerHTML = '<p class="hint">\u0421\u043E\u0431\u044B\u0442\u0438\u0439 \u043F\u043E\u043A\u0430 \u043D\u0435\u0442.</p>';
  }

  chatLog.innerHTML = "";
  if (stateView.chatMessages && stateView.chatMessages.length > 0) {
    stateView.chatMessages.forEach((msg) => {
      const p = document.createElement("p");
      p.className = "chat-entry";
      p.textContent = msg;
      chatLog.appendChild(p);
    });
  }

  newGameButton.classList.toggle("hidden", !stateView.gameOver);

  playersContainer.innerHTML = "";

  const isCircular = stateView.started && !state.isMobile && stateView.players.length >= 2;
  playersContainer.classList.toggle("players--circular", isCircular);

  if (isCircular) {
    const positions = computeTablePositions(stateView.players.length);
    stateView.players.forEach((player, index) => {
      const card = createPlayerCard(player, index, stateView);
      card.style.left = `${positions[index].left}%`;
      card.style.top = `${positions[index].top}%`;
      playersContainer.appendChild(card);
    });
  } else {
    stateView.players.forEach((player, index) => {
      const card = createPlayerCard(player, index, stateView);
      playersContainer.appendChild(card);
    });
  }

  handCards.innerHTML = "";
  if (stateView.yourHand.length === 0) {
    handHint.textContent = "\u0412 \u0440\u0443\u043A\u0435 \u043D\u0435\u0442 \u043A\u0430\u0440\u0442. \u0417\u0430\u0432\u0435\u0440\u0448\u0438\u0442\u0435 \u0445\u043E\u0434 \u0438\u043B\u0438 \u0434\u043E\u0436\u0434\u0438\u0442\u0435\u0441\u044C \u0434\u043E\u0431\u043E\u0440\u0430.";
  } else {
    handHint.textContent = "\u041D\u0430\u0436\u043C\u0438\u0442\u0435 \u043D\u0430 \u043A\u0430\u0440\u0442\u0443, \u0447\u0442\u043E\u0431\u044B \u0441\u044B\u0433\u0440\u0430\u0442\u044C.";
  }

  stateView.yourHand.forEach((card, index) => {
    const element = document.createElement("div");
    const typeClass = getCardTypeClass(card.type);
    const categoryClass = `card--cat-${String(card.category).toLowerCase()}`;
    element.className = `card card--${typeClass} ${categoryClass}`;
    if (
      card.type === "Bang" &&
      stateView.started &&
      !stateView.gameOver &&
      stateView.currentPlayerId === state.playerId &&
      stateView.bangsPlayedThisTurn >= stateView.bangLimit
    ) {
      element.dataset.tooltip = `\u041C\u043E\u0436\u043D\u043E \u0441\u044B\u0433\u0440\u0430\u0442\u044C \u0442\u043E\u043B\u044C\u043A\u043E ${stateView.bangLimit} \u0411\u044D\u043D\u0433! \u0437\u0430 \u0445\u043E\u0434.`;
    }
    const imageHtml = card.imagePath
      ? `<img class="card-image" src="${card.imagePath}" alt="${card.name}" onerror="this.style.display='none'"/>`
      : "";
    const categoryTag = cardCategoryLabels[card.category]
      ? `<span class="tag equip">${cardCategoryLabels[card.category]}</span>`
      : "";
    const suitValueLabel = card.suit
      ? `<span class="suit-badge" style="color:${suitColors[card.suit] || '#fff'}">${formatSuitValue(card)}</span>`
      : "";
    element.innerHTML = `
      <div class="card-inner">
        ${imageHtml}
        <div class="card-info">
          <strong>${card.name} ${suitValueLabel}</strong>
          <small>${card.description}</small>
        </div>
        <div class="card-tags">
          <span class="tag tag--${typeClass}">${cardTypeLabels[card.type] || card.type}</span>
          ${card.requiresTarget ? '<span class="tag tag--target">\u0426\u0435\u043B\u044C</span>' : ""}
          ${categoryTag}
        </div>
      </div>
    `;
    element.addEventListener("click", () => onCardSelected(card, index, element));
    handCards.appendChild(element);
  });

  const me = stateView.players.find((p) => p.id === stateView.yourPublicId);
  const hasPending = !!stateView.pendingAction;
  const isYourTurn = stateView.started && stateView.currentPlayerId === state.playerId;

  if (me && isYourTurn && !hasPending && !stateView.gameOver && !isSpectator) {
    me.equipment.forEach((card, idx) => {
      if (card.category !== "Green" || reactiveGreenTypes.has(card.type) || card.isFresh) return;
      const btn = document.createElement("button");
      btn.className = "green-use-btn";
      btn.textContent = `\u0410\u043A\u0442\u0438\u0432\u0438\u0440\u043E\u0432\u0430\u0442\u044C: ${card.name}`;
      if (activeGreenTargeted.has(card.type)) {
        btn.addEventListener("click", () => showGreenCardTargetOverlay(idx, card));
      } else if (activeGreenUntargeted.has(card.type)) {
        btn.addEventListener("click", () => useGreenCardFn?.(idx, null));
      }
      handCards.appendChild(btn);
    });
  }

  endTurnButton.disabled = !isYourTurn || stateView.gameOver || hasPending || isSpectator;
  startButton.disabled = isSpectator || !isHost;
  newGameButton.disabled = !isHost;

  const myChar = getMyCharacterName(stateView);
  const abilityBase = !isSpectator && isYourTurn && !stateView.gameOver && !hasPending;
  let canUseAbility = false;
  let abilityLabel = "\u0421\u043F\u043E\u0441\u043E\u0431\u043D\u043E\u0441\u0442\u044C";
  if (abilityBase) {
    if (stateView.currentEventName === DOCTOR_EVENT && stateView.yourHand.length >= 2 && me != null && me.hp < me.maxHp) {
      canUseAbility = true;
      abilityLabel = "\u0414\u043E\u043A\u0442\u043E\u0440: -2 \u043A\u0430\u0440\u0442\u044B, +1 \u041E\u0417";
    } else if (myChar === CHAR_SID && stateView.yourHand.length >= 2 && me != null && me.hp < me.maxHp) {
      canUseAbility = true;
      abilityLabel = "\u0421\u0438\u0434 \u041A\u0435\u0442\u0447\u0443\u043C: -2 \u043A\u0430\u0440\u0442\u044B, +1 \u041E\u0417";
    } else if (myChar === CHAR_CHUCK && me != null && me.hp > 1) {
      canUseAbility = true;
      abilityLabel = "\u0427\u0430\u043A \u0412\u0435\u043D\u0433\u0430\u043C: -1 \u041E\u0417, +2 \u043A\u0430\u0440\u0442\u044B";
    } else if (myChar === CHAR_DOC && stateView.yourHand.length >= 2) {
      canUseAbility = true;
      abilityLabel = "\u0414\u043E\u043A \u0425\u043E\u043B\u0438\u0434\u044D\u0439: -2 \u043A\u0430\u0440\u0442\u044B, \u0432\u044B\u0441\u0442\u0440\u0435\u043B";
    } else if (myChar === CHAR_JOSE && stateView.yourHand.some(c => c.category === "Blue")) {
      canUseAbility = true;
      abilityLabel = "\u0425\u043E\u0441\u0435 \u0414\u0435\u043B\u044C\u0433\u0430\u0434\u043E: -1 \u0441\u0438\u043D\u044F\u044F, +2 \u043A\u0430\u0440\u0442\u044B";
    }
  }
  abilityButton.classList.toggle("hidden", !canUseAbility);
  if (canUseAbility) abilityButton.textContent = abilityLabel;

  if (!stateView.started || stateView.currentPlayerId !== state.playerId || hasPending) {
    hideTargetOverlay();
  }
  if (state.selectedCard && (!stateView.yourHand[state.selectedCard.index] || stateView.yourHand[state.selectedCard.index].name !== state.selectedCard.card.name)) {
    hideTargetOverlay();
  }

  if (hasPending) {
    const pa = stateView.pendingAction!;
    if (pa.respondingPlayerId === state.playerId) {
      showResponseOverlay(pa, stateView);
    } else {
      hideResponseOverlay();
      if (!stateView.gameOver) {
        turnInfo.textContent = `\u041E\u0436\u0438\u0434\u0430\u043D\u0438\u0435 \u043E\u0442\u0432\u0435\u0442\u0430 \u043E\u0442 ${pa.respondingPlayerName}...`;
      }
    }
  } else {
    hideResponseOverlay();
  }

  updateMobileState();
};
