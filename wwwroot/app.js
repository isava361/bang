const joinPanel = document.getElementById("joinPanel");
const gamePanel = document.getElementById("gamePanel");
const connectionStatus = document.getElementById("connectionStatus");
const joinButton = document.getElementById("joinButton");
const playerNameInput = document.getElementById("playerName");
const startButton = document.getElementById("startButton");
const endTurnButton = document.getElementById("endTurnButton");
const abilityButton = document.getElementById("abilityButton");
const playersContainer = document.getElementById("players");
const handCards = document.getElementById("handCards");
const handHint = document.getElementById("handHint");
const eventLog = document.getElementById("eventLog");
const chatLog = document.getElementById("chatLog");
const newGameButton = document.getElementById("newGameButton");
const turnInfo = document.getElementById("turnInfo");
const chatInput = document.getElementById("chatInput");
const chatButton = document.getElementById("chatButton");
const targetOverlay = document.getElementById("targetOverlay");
const targetList = document.getElementById("targetList");
const targetPrompt = document.getElementById("targetPrompt");
const cancelTarget = document.getElementById("cancelTarget");
const cardLibrary = document.getElementById("cardLibrary");
const characterLibrary = document.getElementById("characterLibrary");
const responseOverlay = document.getElementById("responseOverlay");
const responseTitle = document.getElementById("responseTitle");
const responsePrompt = document.getElementById("responsePrompt");
const responseCards = document.getElementById("responseCards");
const responsePass = document.getElementById("responsePass");
const abilityOverlay = document.getElementById("abilityOverlay");
const abilityCards = document.getElementById("abilityCards");
const abilityConfirm = document.getElementById("abilityConfirm");
const cancelAbility = document.getElementById("cancelAbility");

let playerId = null;
let currentState = null;
let selectedCard = null;
let abilitySelectedIndices = [];

const cardsReference = [
  {
    name: "Bang!",
    type: "Bang",
    description: "Deal 1 damage (2 if you're Slab the Killer).",
    imagePath: "/assets/cards/bang.png",
  },
  {
    name: "Missed!",
    type: "Missed",
    description: "Play when shot to negate the damage.",
    imagePath: "/assets/cards/missed.png",
  },
  {
    name: "Beer",
    type: "Beer",
    description: "Recover 1 HP. Disabled with 2 players left.",
    imagePath: "/assets/cards/beer.png",
  },
  {
    name: "Gatling",
    type: "Gatling",
    description: "Each other player must play a Missed! or take 1 damage.",
    imagePath: "/assets/cards/gatling.png",
  },
  {
    name: "Stagecoach",
    type: "Stagecoach",
    description: "Draw 2 cards.",
    imagePath: "/assets/cards/stagecoach.png",
  },
  {
    name: "Cat Balou",
    type: "CatBalou",
    description: "Force a target to discard a card (hand or equipment).",
    imagePath: "/assets/cards/cat_balou.png",
  },
  {
    name: "Indians!",
    type: "Indians",
    description: "Each other player must discard a Bang! or take 1 damage.",
    imagePath: "/assets/cards/indians.png",
  },
  {
    name: "Duel",
    type: "Duel",
    description: "Challenge a player — alternate discarding Bang! cards.",
    imagePath: "/assets/cards/duel.png",
  },
  {
    name: "Panic!",
    type: "Panic",
    description: "Steal a card from a player at distance 1.",
    imagePath: "/assets/cards/panic.png",
  },
  {
    name: "Saloon",
    type: "Saloon",
    description: "All living players heal 1 HP.",
    imagePath: "/assets/cards/saloon.png",
  },
  {
    name: "Wells Fargo",
    type: "WellsFargo",
    description: "Draw 3 cards.",
    imagePath: "/assets/cards/wells_fargo.png",
  },
  {
    name: "General Store",
    type: "GeneralStore",
    description: "Reveal cards equal to alive players. Each picks one.",
    imagePath: "/assets/cards/general_store.png",
  },
  {
    name: "Barrel",
    type: "Barrel",
    description: "25% chance to auto-dodge shots (50% for Lucky Duke).",
    imagePath: "/assets/cards/barrel.png",
  },
  {
    name: "Mustang",
    type: "Mustang",
    description: "Others see you at distance +1.",
    imagePath: "/assets/cards/mustang.png",
  },
  {
    name: "Scope",
    type: "Scope",
    description: "You see others at distance -1.",
    imagePath: "/assets/cards/scope.png",
  },
  {
    name: "Volcanic",
    type: "Volcanic",
    description: "Weapon (range 1). Unlimited Bang! per turn.",
    imagePath: "/assets/cards/volcanic.png",
  },
  {
    name: "Schofield",
    type: "Schofield",
    description: "Weapon (range 2).",
    imagePath: "/assets/cards/schofield.png",
  },
  {
    name: "Remington",
    type: "Remington",
    description: "Weapon (range 3).",
    imagePath: "/assets/cards/remington.png",
  },
  {
    name: "Rev. Carabine",
    type: "RevCarabine",
    description: "Weapon (range 4).",
    imagePath: "/assets/cards/rev_carabine.png",
  },
  {
    name: "Winchester",
    type: "Winchester",
    description: "Weapon (range 5).",
    imagePath: "/assets/cards/winchester.png",
  },
];

const charactersReference = [
  {
    name: "Lucky Duke",
    description: "Barrel checks succeed 50% instead of 25%.",
    portraitPath: "/assets/characters/lucky_duke.png",
  },
  {
    name: "Slab the Killer",
    description: "Your Bang! cards deal 2 damage.",
    portraitPath: "/assets/characters/slab_the_killer.png",
  },
  {
    name: "El Gringo",
    description: "When hit, draw a card from the attacker's hand.",
    portraitPath: "/assets/characters/el_gringo.png",
  },
  {
    name: "Suzy Lafayette",
    description: "Whenever your hand becomes empty, draw 1 card.",
    portraitPath: "/assets/characters/suzy_lafayette.png",
  },
  {
    name: "Rose Doolan",
    description: "Built-in Scope: you see others at distance -1.",
    portraitPath: "/assets/characters/rose_doolan.png",
  },
  {
    name: "Jesse Jones",
    description: "Draw your first card from a chosen player's hand.",
    portraitPath: "/assets/characters/jesse_jones.png",
  },
  {
    name: "Bart Cassidy",
    description: "Each time you take damage, draw 1 card from the deck.",
    portraitPath: "/assets/characters/bart_cassidy.png",
  },
  {
    name: "Paul Regret",
    description: "Built-in Mustang: others see you at distance +1.",
    portraitPath: "/assets/characters/paul_regret.png",
  },
  {
    name: "Calamity Janet",
    description: "Use Bang! as Missed! and Missed! as Bang!.",
    portraitPath: "/assets/characters/calamity_janet.png",
  },
  {
    name: "Kit Carlson",
    description: "Look at the top 3 cards, keep 2, put 1 back.",
    portraitPath: "/assets/characters/kit_carlson.png",
  },
  {
    name: "Willy the Kid",
    description: "You can play unlimited Bang! cards per turn.",
    portraitPath: "/assets/characters/willy_the_kid.png",
  },
  {
    name: "Sid Ketchum",
    description: "Discard 2 cards to regain 1 HP (on your turn).",
    portraitPath: "/assets/characters/sid_ketchum.png",
  },
  {
    name: "Vulture Sam",
    description: "When a player is eliminated, take all their cards.",
    portraitPath: "/assets/characters/vulture_sam.png",
  },
  {
    name: "Pedro Ramirez",
    description: "Draw your first card from the discard pile.",
    portraitPath: "/assets/characters/pedro_ramirez.png",
  },
];

const renderLibrary = () => {
  cardLibrary.innerHTML = "";
  cardsReference.forEach((card) => {
    const item = document.createElement("div");
    item.className = "library-item";
    item.innerHTML = `
      <div class="library-row">
        <img class="library-image" src="${card.imagePath}" alt="${card.name}" onerror="this.style.display='none'"/>
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
        <img class="library-image" src="${character.portraitPath}" alt="${character.name}" onerror="this.style.display='none'"/>
        <div>
          <strong>${character.name}</strong>
          <p>${character.description}</p>
        </div>
      </div>
    `;
    characterLibrary.appendChild(item);
  });
};

const setStatus = (text) => {
  connectionStatus.textContent = text;
};

const getMyCharacterName = (state) => {
  const me = state.players.find((p) => p.id === playerId);
  return me ? me.characterName : "";
};

const updateState = (state) => {
  if (!state) {
    return;
  }

  currentState = state;
  if (state.gameOver) {
    turnInfo.textContent = state.winnerMessage || "Game over.";
  } else {
    turnInfo.textContent = state.started
      ? `Turn: ${state.currentPlayerName}`
      : "Waiting for players to start...";
  }
  eventLog.innerHTML = "";
  if (state.eventLog && state.eventLog.length > 0) {
    state.eventLog.forEach((evt) => {
      const p = document.createElement("p");
      p.className = "event-entry";
      p.textContent = evt;
      eventLog.appendChild(p);
    });
  } else {
    eventLog.innerHTML = '<p class="hint">No events yet.</p>';
  }

  chatLog.innerHTML = "";
  if (state.chatMessages && state.chatMessages.length > 0) {
    state.chatMessages.forEach((msg) => {
      const p = document.createElement("p");
      p.className = "chat-entry";
      p.textContent = msg;
      chatLog.appendChild(p);
    });
  }

  if (newGameButton) {
    newGameButton.classList.toggle("hidden", !state.gameOver);
  }

  playersContainer.innerHTML = "";

  state.players.forEach((player) => {
    const card = document.createElement("div");
    card.className = "player-card";
    if (player.id === state.currentPlayerId) {
      card.classList.add("active");
    }
    if (!player.isAlive) {
      card.classList.add("out");
    }

    const portraitHtml = player.characterPortrait
      ? `<img class="player-portrait" src="${player.characterPortrait}" alt="${player.characterName}" onerror="this.style.display='none'"/>`
      : "";

    const equipHtml = player.equipment && player.equipment.length > 0
      ? player.equipment.map((e) => `<span class="equip-tag">${e.name}</span>`).join(" ")
      : "";

    const distanceHtml = state.distances && state.distances[player.id] != null
      ? `<small class="distance-label">Distance: ${state.distances[player.id]}</small>`
      : "";

    card.innerHTML = `
      <div class="player-header">
        ${portraitHtml}
        <div>
          <strong>${player.name}</strong>
          <p>${player.characterName}</p>
        </div>
      </div>
      <small>${player.characterDescription}</small>
      <p class="role-line">Role: ${player.role}</p>
      <p>HP: ${player.hp} / ${player.maxHp} | Cards: ${player.handCount}</p>
      ${equipHtml ? `<div class="equip-row">${equipHtml}</div>` : ""}
      ${distanceHtml}
    `;
    playersContainer.appendChild(card);
  });

  handCards.innerHTML = "";
  if (state.yourHand.length === 0) {
    handHint.textContent = "No cards in hand. End your turn or wait for draws.";
  } else {
    handHint.textContent = "Click a card to play it.";
  }

  state.yourHand.forEach((card, index) => {
    const element = document.createElement("div");
    element.className = "card";
    if (
      card.type === "Bang" &&
      state.started &&
      !state.gameOver &&
      state.currentPlayerId === playerId &&
      state.bangsPlayedThisTurn >= state.bangLimit
    ) {
      element.dataset.tooltip = `You can only play ${state.bangLimit} Bang! each turn.`;
    }
    const imageHtml = card.imagePath
      ? `<img class="card-image" src="${card.imagePath}" alt="${card.name}" onerror="this.style.display='none'"/>`
      : "";
    const categoryTag = card.category === "Blue" || card.category === "Weapon"
      ? `<span class="tag equip">${card.category}</span>`
      : "";
    element.innerHTML = `
      <div>
        ${imageHtml}
        <strong>${card.name}</strong>
        <small>${card.description}</small>
      </div>
      <div>
        <span class="tag">${card.type}</span>
        ${card.requiresTarget ? "<span class=\"tag\">Target</span>" : ""}
        ${categoryTag}
      </div>
    `;
    element.addEventListener("click", () => onCardSelected(card, index, element));
    handCards.appendChild(element);
  });

  const hasPending = !!state.pendingAction;
  const isYourTurn = state.started && state.currentPlayerId === playerId;
  endTurnButton.disabled = !isYourTurn || state.gameOver || hasPending;

  if (abilityButton) {
    const myChar = getMyCharacterName(state);
    const me = state.players.find((p) => p.id === playerId);
    const canUseAbility = isYourTurn && !state.gameOver && !hasPending &&
      myChar === "Sid Ketchum" && state.yourHand.length >= 2 &&
      me && me.hp < me.maxHp;
    abilityButton.classList.toggle("hidden", !canUseAbility);
  }

  if (!state.started || state.currentPlayerId !== playerId || hasPending) {
    hideTargetOverlay();
  }
  if (selectedCard && (!state.yourHand[selectedCard.index] || state.yourHand[selectedCard.index].name !== selectedCard.card.name)) {
    hideTargetOverlay();
  }

  if (hasPending) {
    const pa = state.pendingAction;
    if (pa.respondingPlayerId === playerId) {
      showResponseOverlay(pa, state);
    } else {
      hideResponseOverlay();
      if (!state.gameOver) {
        turnInfo.textContent = `Waiting for ${pa.respondingPlayerName} to respond...`;
      }
    }
  } else {
    hideResponseOverlay();
  }
};

const showTargetOverlay = (card, index) => {
  if (!currentState) {
    return;
  }

  selectedCard = { card, index };
  targetList.innerHTML = "";
  targetPrompt.textContent = card.targetHint || "Choose who to target.";

  const effectiveType = card.type === "Missed" && getMyCharacterName(currentState) === "Calamity Janet"
    ? "Bang"
    : card.type;

  const availableTargets = currentState.players
    .filter((player) => player.id !== playerId && player.isAlive)
    .map((player) => {
      const dist = currentState.distances ? currentState.distances[player.id] : null;
      let outOfRange = false;
      if (effectiveType === "Bang" && dist != null && dist > currentState.weaponRange) {
        outOfRange = true;
      }
      if (effectiveType === "Panic" && dist != null && dist > 1) {
        outOfRange = true;
      }
      return { ...player, distance: dist, outOfRange };
    });

  if (availableTargets.length === 0) {
    targetPrompt.textContent = "No available targets yet. Ask another player to join.";
    const empty = document.createElement("div");
    empty.className = "hint";
    empty.textContent = "Targets will appear once another player joins the table.";
    targetList.appendChild(empty);
  } else {
    availableTargets.forEach((player) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const distLabel = player.distance != null ? ` [dist: ${player.distance}]` : "";
      button.textContent = `${player.name} (${player.characterName})${distLabel}`;
      if (player.outOfRange) {
        button.disabled = true;
        button.title = "Out of range";
      } else {
        button.addEventListener("click", () => playCard(index, player.id));
      }
      targetList.appendChild(button);
    });
  }

  targetOverlay.classList.remove("hidden");
};

const hideTargetOverlay = () => {
  selectedCard = null;
  targetOverlay.classList.add("hidden");
};

const showResponseOverlay = (pendingAction, state) => {
  responseCards.innerHTML = "";
  responsePrompt.textContent = pendingAction.message;

  const type = pendingAction.type;

  if (type === "GeneralStorePick" || type === "KitCarlsonPick") {
    responseTitle.textContent = type === "GeneralStorePick" ? "General Store" : "Kit Carlson";
    responsePass.classList.add("hidden");
    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = card.name;
        button.addEventListener("click", () => respondToAction("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "DiscardExcess") {
    responseTitle.textContent = "Discard";
    responsePass.classList.add("hidden");
    state.yourHand.forEach((card, idx) => {
      const button = document.createElement("button");
      button.className = "target-button";
      button.textContent = card.name;
      button.addEventListener("click", () => respondToAction("play_card", idx));
      responseCards.appendChild(button);
    });
  } else if (type === "ChooseStealSource") {
    responseTitle.textContent = "Choose Target";
    responsePass.classList.add("hidden");

    const handButton = document.createElement("button");
    handButton.className = "target-button";
    handButton.textContent = "Random card from hand";
    handButton.addEventListener("click", () => respondToAction("hand", null));
    responseCards.appendChild(handButton);

    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `Equipment: ${card.name}`;
        button.addEventListener("click", () => respondToAction("equipment", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "JesseJonesSteal") {
    responseTitle.textContent = "Jesse Jones";
    responsePass.classList.add("hidden");

    const targets = state.players.filter(
      (p) => p.id !== playerId && p.isAlive && p.handCount > 0
    );
    if (targets.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "No players with cards to draw from.";
      responseCards.appendChild(hint);
    } else {
      targets.forEach((player) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `${player.name} (${player.handCount} cards)`;
        button.addEventListener("click", () => respondToAction("steal", null, player.id));
        responseCards.appendChild(button);
      });
    }
  } else {
    const isIndians = type === "IndiansDefense";
    const isDuel = type === "DuelChallenge";
    const myChar = getMyCharacterName(state);
    const isJanet = myChar === "Calamity Janet";

    let requiredTypes;
    if (isIndians || isDuel) {
      requiredTypes = isJanet ? ["Bang", "Missed"] : ["Bang"];
    } else {
      requiredTypes = isJanet ? ["Missed", "Bang"] : ["Missed"];
    }
    const requiredName = requiredTypes.map((t) => t === "Bang" ? "Bang!" : "Missed!").join("/");

    responseTitle.textContent = isDuel ? "Duel" : "Defend";
    responsePass.classList.remove("hidden");
    responsePass.textContent = isDuel ? "Give up" : "Take the hit";

    const validCards = state.yourHand
      .map((card, idx) => ({ card, idx }))
      .filter(({ card }) => requiredTypes.includes(card.type));

    if (validCards.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = `You have no ${requiredName} cards to play.`;
      responseCards.appendChild(hint);
    } else {
      validCards.forEach(({ card, idx }) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `Play ${card.name}`;
        button.addEventListener("click", () => respondToAction("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  }

  responseOverlay.classList.remove("hidden");
};

const hideResponseOverlay = () => {
  responseOverlay.classList.add("hidden");
};

const respondToAction = async (responseType, cardIndex, targetId) => {
  if (!playerId) return;
  try {
    const data = await apiPost("/api/respond", {
      playerId,
      responseType,
      cardIndex,
      targetId: targetId || null,
    });
    hideResponseOverlay();
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

const onCardSelected = (card, index, element) => {
  if (!currentState || !playerId) {
    return;
  }

  if (!currentState.started) {
    setStatus("Start the game before playing cards.");
    return;
  }

  if (currentState.pendingAction) {
    setStatus("Waiting for a player to respond.");
    return;
  }

  if (currentState.currentPlayerId !== playerId) {
    setStatus("Wait for your turn to play a card.");
    return;
  }

  const myChar = getMyCharacterName(currentState);
  const effectiveType = card.type === "Missed" && myChar === "Calamity Janet" ? "Bang" : card.type;

  if (effectiveType === "Bang" && currentState.bangsPlayedThisTurn >= currentState.bangLimit) {
    showCardTooltip(element, `You can only play ${currentState.bangLimit} Bang! each turn.`);
    return;
  }

  const needsTarget = card.requiresTarget || effectiveType === "Bang";
  if (needsTarget) {
    showTargetOverlay(card, index);
  } else {
    playCard(index, null);
  }
};

const showCardTooltip = (element, message) => {
  if (!element) {
    return;
  }

  const tooltip = document.createElement("div");
  tooltip.className = "card-tooltip";
  tooltip.textContent = message;
  element.appendChild(tooltip);

  setTimeout(() => {
    tooltip.remove();
  }, 2000);
};

const showAbilityOverlay = () => {
  if (!currentState) return;
  abilitySelectedIndices = [];
  abilityCards.innerHTML = "";
  abilityConfirm.disabled = true;

  currentState.yourHand.forEach((card, idx) => {
    const button = document.createElement("button");
    button.className = "target-button";
    button.textContent = card.name;
    button.addEventListener("click", () => {
      if (abilitySelectedIndices.includes(idx)) {
        abilitySelectedIndices = abilitySelectedIndices.filter((i) => i !== idx);
        button.classList.remove("selected");
      } else if (abilitySelectedIndices.length < 2) {
        abilitySelectedIndices.push(idx);
        button.classList.add("selected");
      }
      abilityConfirm.disabled = abilitySelectedIndices.length !== 2;
    });
    abilityCards.appendChild(button);
  });

  abilityOverlay.classList.remove("hidden");
};

const hideAbilityOverlay = () => {
  abilityOverlay.classList.add("hidden");
  abilitySelectedIndices = [];
};

const useAbility = async () => {
  if (!playerId || abilitySelectedIndices.length !== 2) return;
  try {
    const data = await apiPost("/api/ability", {
      playerId,
      cardIndices: abilitySelectedIndices,
    });
    hideAbilityOverlay();
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

const apiPost = async (url, body) => {
  const response = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  const payload = await response.json();
  if (!response.ok) {
    throw new Error(payload.message || "Request failed.");
  }

  return payload.data;
};

const joinGame = async () => {
  const name = playerNameInput.value.trim();
  if (!name) {
    setStatus("Enter a name to join.");
    return;
  }

  try {
    const data = await apiPost("/api/join", { name });
    playerId = data.playerId;
    localStorage.setItem("bangPlayerId", playerId);
    localStorage.setItem("bangPlayerName", name);
    joinPanel.classList.add("hidden");
    gamePanel.classList.remove("hidden");
    setStatus(`Connected as ${name}`);
    updateState(data.state);
  } catch (error) {
    setStatus(error.message);
  }
};

const startGame = async () => {
  if (!playerId) {
    return;
  }

  try {
    const data = await apiPost("/api/start", { playerId });
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

const playCard = async (index, targetId) => {
  if (!playerId) {
    return;
  }

  try {
    const data = await apiPost("/api/play", {
      playerId,
      cardIndex: index,
      targetId,
    });
    hideTargetOverlay();
    updateState(data);
  } catch (error) {
    hideTargetOverlay();
    setStatus(error.message);
  }
};

const endTurn = async () => {
  if (!playerId) {
    return;
  }

  try {
    const data = await apiPost("/api/end", { playerId });
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

const sendChat = async () => {
  if (!playerId) {
    return;
  }

  const text = chatInput.value.trim();
  if (!text) {
    return;
  }

  try {
    const data = await apiPost("/api/chat", { playerId, text });
    chatInput.value = "";
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

const refreshState = async () => {
  if (!playerId) {
    return;
  }

  const response = await fetch(`/api/state?playerId=${playerId}`);
  const payload = await response.json();
  if (response.ok) {
    updateState(payload.data);
  }
};

const newGame = async () => {
  if (!playerId) return;
  try {
    const data = await apiPost("/api/newgame", { playerId });
    updateState(data);
  } catch (error) {
    setStatus(error.message);
  }
};

joinButton.addEventListener("click", joinGame);
startButton.addEventListener("click", startGame);
endTurnButton.addEventListener("click", endTurn);
chatButton.addEventListener("click", sendChat);
cancelTarget.addEventListener("click", hideTargetOverlay);
responsePass.addEventListener("click", () => respondToAction("pass", null));
if (newGameButton) {
  newGameButton.addEventListener("click", newGame);
}

if (abilityButton) {
  abilityButton.addEventListener("click", showAbilityOverlay);
}
if (cancelAbility) {
  cancelAbility.addEventListener("click", hideAbilityOverlay);
}
if (abilityConfirm) {
  abilityConfirm.addEventListener("click", useAbility);
}

playerNameInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    joinGame();
  }
});

chatInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    sendChat();
  }
});

renderLibrary();

const tryReconnect = async () => {
  const savedId = localStorage.getItem("bangPlayerId");
  const savedName = localStorage.getItem("bangPlayerName");
  if (!savedId) return;
  try {
    const response = await fetch(`/api/reconnect?playerId=${savedId}`);
    const payload = await response.json();
    if (response.ok && payload.data) {
      playerId = savedId;
      joinPanel.classList.add("hidden");
      gamePanel.classList.remove("hidden");
      setStatus(`Reconnected as ${savedName || "player"}`);
      updateState(payload.data);
    } else {
      localStorage.removeItem("bangPlayerId");
      localStorage.removeItem("bangPlayerName");
    }
  } catch {
    localStorage.removeItem("bangPlayerId");
    localStorage.removeItem("bangPlayerName");
  }
};

tryReconnect();
setInterval(refreshState, 1000);
