const joinPanel = document.getElementById("joinPanel");
const gamePanel = document.getElementById("gamePanel");
const connectionStatus = document.getElementById("connectionStatus");
const joinButton = document.getElementById("joinButton");
const playerNameInput = document.getElementById("playerName");
const startButton = document.getElementById("startButton");
const endTurnButton = document.getElementById("endTurnButton");
const playersContainer = document.getElementById("players");
const handCards = document.getElementById("handCards");
const handHint = document.getElementById("handHint");
const lastEvent = document.getElementById("lastEvent");
const turnInfo = document.getElementById("turnInfo");
const chatInput = document.getElementById("chatInput");
const chatButton = document.getElementById("chatButton");
const targetOverlay = document.getElementById("targetOverlay");
const targetList = document.getElementById("targetList");
const targetPrompt = document.getElementById("targetPrompt");
const cancelTarget = document.getElementById("cancelTarget");
const cardLibrary = document.getElementById("cardLibrary");
const characterLibrary = document.getElementById("characterLibrary");

let playerId = null;
let currentState = null;
let selectedCard = null;

const cardsReference = [
  {
    name: "Bang!",
    type: "Bang",
    description: "Deal 1 damage (2 if you're Slab the Killer).",
    imagePath: "/assets/cards/bang.png",
  },
  {
    name: "Beer",
    type: "Beer",
    description: "Recover 1 HP.",
    imagePath: "/assets/cards/beer.png",
  },
  {
    name: "Gatling",
    type: "Gatling",
    description: "Deal 1 damage to every other player.",
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
    description: "Force a target to discard a random card.",
    imagePath: "/assets/cards/cat_balou.png",
  },
  {
    name: "Indians!",
    type: "Indians",
    description: "Deal 1 damage to every other player.",
    imagePath: "/assets/cards/indians.png",
  },
  {
    name: "Duel",
    type: "Duel",
    description: "Target player takes 1 damage.",
    imagePath: "/assets/cards/duel.png",
  },
  {
    name: "Panic!",
    type: "Panic",
    description: "Steal a random card from a target.",
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
    description: "Draw 2 cards.",
    imagePath: "/assets/cards/general_store.png",
  },
];

const charactersReference = [
  {
    name: "Lucky Duke",
    description: "Start each turn by drawing 3 cards instead of 2.",
    portraitPath: "/assets/characters/lucky_duke.png",
  },
  {
    name: "Slab the Killer",
    description: "Your Bang! cards deal 2 damage.",
    portraitPath: "/assets/characters/slab_the_killer.png",
  },
  {
    name: "El Gringo",
    description: "Whenever you are hit, draw 1 card.",
    portraitPath: "/assets/characters/el_gringo.png",
  },
  {
    name: "Suzy Lafayette",
    description: "When you end your turn with no cards, draw 1.",
    portraitPath: "/assets/characters/suzy_lafayette.png",
  },
  {
    name: "Rose Doolan",
    description: "Steady aim gives you +1 max HP.",
    portraitPath: "/assets/characters/rose_doolan.png",
  },
  {
    name: "Jesse Jones",
    description: "Draw 3 cards at the start of your turn.",
    portraitPath: "/assets/characters/jesse_jones.png",
  },
  {
    name: "Bart Cassidy",
    description: "Every time you take damage, draw 1 card.",
    portraitPath: "/assets/characters/bart_cassidy.png",
  },
  {
    name: "Paul Regret",
    description: "Tougher than he looks: +1 max HP.",
    portraitPath: "/assets/characters/paul_regret.png",
  },
  {
    name: "Calamity Janet",
    description: "Draw 1 when your hand empties.",
    portraitPath: "/assets/characters/calamity_janet.png",
  },
  {
    name: "Kit Carlson",
    description: "Draw 3 cards at turn start.",
    portraitPath: "/assets/characters/kit_carlson.png",
  },
  {
    name: "Willy the Kid",
    description: "Bang! deals 2 damage.",
    portraitPath: "/assets/characters/willy_the_kid.png",
  },
  {
    name: "Sid Ketchum",
    description: "Draw 1 card when hit.",
    portraitPath: "/assets/characters/sid_ketchum.png",
  },
  {
    name: "Vulture Sam",
    description: "Draw 3 cards at the start of your turn.",
    portraitPath: "/assets/characters/vulture_sam.png",
  },
  {
    name: "Pedro Ramirez",
    description: "Hardy ranger: +1 max HP.",
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

const updateState = (state) => {
  if (!state) {
    return;
  }

  currentState = state;
  turnInfo.textContent = state.started
    ? `Turn: ${state.currentPlayerName}`
    : "Waiting for players to start...";
  lastEvent.textContent = state.lastEvent ?? "No events yet.";
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
    card.innerHTML = `
      <div class="player-header">
        ${portraitHtml}
        <div>
          <strong>${player.name}</strong>
          <p>${player.characterName}</p>
        </div>
      </div>
      <small>${player.characterDescription}</small>
      <p>HP: ${player.hp} / ${player.maxHp}</p>
      <small>ID: ${player.id.slice(0, 6)}</small>
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
    const imageHtml = card.imagePath
      ? `<img class="card-image" src="${card.imagePath}" alt="${card.name}" onerror="this.style.display='none'"/>`
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
      </div>
    `;
    element.addEventListener("click", () => onCardSelected(card, index));
    handCards.appendChild(element);
  });

  const isYourTurn = state.started && state.currentPlayerId === playerId;
  endTurnButton.disabled = !isYourTurn;

  if (!state.started || state.currentPlayerId !== playerId) {
    hideTargetOverlay();
  }
  if (selectedCard && (!state.yourHand[selectedCard.index] || state.yourHand[selectedCard.index].name !== selectedCard.card.name)) {
    hideTargetOverlay();
  }
};

const showTargetOverlay = (card, index) => {
  if (!currentState) {
    return;
  }

  selectedCard = { card, index };
  targetList.innerHTML = "";
  targetPrompt.textContent = card.targetHint || "Choose who to target.";

  const availableTargets = currentState.players
    .filter((player) => player.id !== playerId && player.isAlive)
    .map((player) => player);

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
      button.textContent = `${player.name} (${player.characterName})`;
      button.addEventListener("click", () => playCard(index, player.id));
      targetList.appendChild(button);
    });
  }

  targetOverlay.classList.remove("hidden");
};

const hideTargetOverlay = () => {
  selectedCard = null;
  targetOverlay.classList.add("hidden");
};

const onCardSelected = (card, index) => {
  if (!currentState || !playerId) {
    return;
  }

  if (!currentState.started) {
    setStatus("Start the game before playing cards.");
    return;
  }

  if (currentState.currentPlayerId !== playerId) {
    setStatus("Wait for your turn to play a card.");
    return;
  }

  if (card.requiresTarget) {
    showTargetOverlay(card, index);
  } else {
    playCard(index, null);
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

joinButton.addEventListener("click", joinGame);
startButton.addEventListener("click", startGame);
endTurnButton.addEventListener("click", endTurn);
chatButton.addEventListener("click", sendChat);
cancelTarget.addEventListener("click", hideTargetOverlay);

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
setInterval(refreshState, 4000);
