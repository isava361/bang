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
const lobbyPanel = document.getElementById("lobbyPanel");
const createRoomButton = document.getElementById("createRoomButton");
const roomCodeInput = document.getElementById("roomCodeInput");
const joinRoomButton = document.getElementById("joinRoomButton");
const roomListContainer = document.getElementById("roomList");
const leaveButton = document.getElementById("leaveButton");
const renameButton = document.getElementById("renameButton");
const spectatorBanner = document.getElementById("spectatorBanner");
const roomCodeBadge = document.getElementById("roomCodeBadge");

let playerId = null;
let roomCode = null;
let currentState = null;
let lastStateJson = null;
let selectedCard = null;
let abilitySelectedIndices = [];
let connection = null;

const computeTablePositions = (count) => {
  const positions = [];
  for (let i = 0; i < count; i++) {
    const angle = (-Math.PI / 2) - (2 * Math.PI * i) / count;
    positions.push({
      left: 50 + 42 * Math.cos(angle),
      top: 50 - 40 * Math.sin(angle)
    });
  }
  return positions;
};

const suitSymbols = { Spades: "\u2660", Hearts: "\u2665", Diamonds: "\u2666", Clubs: "\u2663" };
const suitColors = { Spades: "#a0a0b0", Hearts: "#e04040", Diamonds: "#e04040", Clubs: "#a0a0b0" };
const formatCountLabel = (count, singular, few, many) => {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return singular;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
  return many;
};
const cardTypeLabels = {
  Bang: "Бэнг!",
  Missed: "Мимо!",
  Beer: "Пиво",
  Gatling: "Гатлинг",
  Stagecoach: "Дилижанс",
  CatBalou: "Красотка",
  Indians: "Индейцы!",
  Duel: "Дуэль",
  Panic: "Паника!",
  Saloon: "Салун",
  WellsFargo: "Уэллс Фарго",
  GeneralStore: "Магазин",
  Barrel: "Бочка",
  Mustang: "Мустанг",
  Scope: "Прицел",
  Volcanic: "Вулканик",
  Schofield: "Скофилд",
  Remington: "Ремингтон",
  RevCarabine: "Карабин",
  Winchester: "Винчестер",
  Jail: "Тюрьма",
  Dynamite: "Динамит",
};
const cardCategoryLabels = {
  Brown: "Коричневая",
  Blue: "Синяя",
  Weapon: "Оружие",
};

const formatCardValue = (value) => {
  if (value === 11) return "J";
  if (value === 12) return "Q";
  if (value === 13) return "K";
  if (value === 14) return "A";
  return value.toString();
};

const formatSuitValue = (card) => {
  if (!card.suit) return "";
  const sym = suitSymbols[card.suit] || "?";
  const val = formatCardValue(card.value);
  return `${val}${sym}`;
};

const cardsReference = [
  {
    name: "Бэнг!",
    type: "Bang",
    description: "Нанесите 1 урон (2, если вы Слэб Убийца).",
    imagePath: "/assets/cards/bang.png",
  },
  {
    name: "Мимо!",
    type: "Missed",
    description: "Сыграйте в ответ на выстрел, чтобы отменить урон.",
    imagePath: "/assets/cards/missed.png",
  },
  {
    name: "Пиво",
    type: "Beer",
    description: "Восстановите 1 ОЗ. Недоступно, когда осталось 2 игрока.",
    imagePath: "/assets/cards/beer.png",
  },
  {
    name: "Гатлинг",
    type: "Gatling",
    description: "Каждый другой игрок должен сыграть Мимо! или получить 1 урон.",
    imagePath: "/assets/cards/gatling.png",
  },
  {
    name: "Дилижанс",
    type: "Stagecoach",
    description: "Доберите 2 карты.",
    imagePath: "/assets/cards/stagecoach.png",
  },
  {
    name: "Красотка",
    type: "CatBalou",
    description: "Заставьте цель сбросить карту (рука или снаряжение).",
    imagePath: "/assets/cards/cat_balou.png",
  },
  {
    name: "Индейцы!",
    type: "Indians",
    description: "Каждый другой игрок должен сбросить Бэнг! или получить 1 урон.",
    imagePath: "/assets/cards/indians.png",
  },
  {
    name: "Дуэль",
    type: "Duel",
    description: "Вызовите игрока на дуэль — по очереди сбрасывайте Бэнг!.",
    imagePath: "/assets/cards/duel.png",
  },
  {
    name: "Паника!",
    type: "Panic",
    description: "Украдите карту у игрока на дистанции 1.",
    imagePath: "/assets/cards/panic.png",
  },
  {
    name: "Салун",
    type: "Saloon",
    description: "Все живые игроки лечатся на 1 ОЗ.",
    imagePath: "/assets/cards/saloon.png",
  },
  {
    name: "Уэллс Фарго",
    type: "WellsFargo",
    description: "Доберите 3 карты.",
    imagePath: "/assets/cards/wells_fargo.png",
  },
  {
    name: "Магазин",
    type: "GeneralStore",
    description: "Откройте карты по числу живых игроков. Каждый выбирает одну.",
    imagePath: "/assets/cards/general_store.png",
  },
  {
    name: "Бочка",
    type: "Barrel",
    description: "При выстреле, «проверка» — если червы, выстрел избегается.",
    imagePath: "/assets/cards/barrel.png",
  },
  {
    name: "Мустанг",
    type: "Mustang",
    description: "Другие видят вас на дистанции +1.",
    imagePath: "/assets/cards/mustang.png",
  },
  {
    name: "Прицел",
    type: "Scope",
    description: "Вы видите других на дистанции -1.",
    imagePath: "/assets/cards/scope.png",
  },
  {
    name: "Вулканик",
    type: "Volcanic",
    description: "Оружие (дальность 1). Можно играть Бэнг! без ограничения за ход.",
    imagePath: "/assets/cards/volcanic.png",
  },
  {
    name: "Скофилд",
    type: "Schofield",
    description: "Оружие (дальность 2).",
    imagePath: "/assets/cards/schofield.png",
  },
  {
    name: "Ремингтон",
    type: "Remington",
    description: "Оружие (дальность 3).",
    imagePath: "/assets/cards/remington.png",
  },
  {
    name: "Карабин",
    type: "RevCarabine",
    description: "Оружие (дальность 4).",
    imagePath: "/assets/cards/rev_carabine.png",
  },
  {
    name: "Винчестер",
    type: "Winchester",
    description: "Оружие (дальность 5).",
    imagePath: "/assets/cards/winchester.png",
  },
];

const charactersReference = [
  {
    name: "Счастливчик Дьюк",
    description: "При «проверке» откройте 2 карты и выберите лучший результат.",
    portraitPath: "/assets/characters/lucky_duke.png",
  },
  {
    name: "Слэб Убийца",
    description: "Ваши Бэнг! наносят 2 урона.",
    portraitPath: "/assets/characters/slab_the_killer.png",
  },
  {
    name: "Эль Гринго",
    description: "При получении урона возьмите карту из руки атакующего.",
    portraitPath: "/assets/characters/el_gringo.png",
  },
  {
    name: "Сьюзи Лафайет",
    description: "Когда рука становится пустой, возьмите 1 карту.",
    portraitPath: "/assets/characters/suzy_lafayette.png",
  },
  {
    name: "Роуз Дулан",
    description: "Встроенный Прицел: вы видите других на дистанции -1.",
    portraitPath: "/assets/characters/rose_doolan.png",
  },
  {
    name: "Джесси Джонс",
    description: "Первую карту берите из руки выбранного игрока.",
    portraitPath: "/assets/characters/jesse_jones.png",
  },
  {
    name: "Барт Кэссиди",
    description: "Каждый раз при получении урона берите 1 карту из колоды.",
    portraitPath: "/assets/characters/bart_cassidy.png",
  },
  {
    name: "Пол Регрет",
    description: "Встроенный Мустанг: другие видят вас на дистанции +1.",
    portraitPath: "/assets/characters/paul_regret.png",
  },
  {
    name: "Каламити Джанет",
    description: "Используйте Бэнг! как Мимо! и Мимо! как Бэнг!.",
    portraitPath: "/assets/characters/calamity_janet.png",
  },
  {
    name: "Кит Карлсон",
    description: "Посмотрите 3 верхние карты, оставьте 2, 1 верните.",
    portraitPath: "/assets/characters/kit_carlson.png",
  },
  {
    name: "Уилли Кид",
    description: "Можно играть Бэнг! без ограничения за ход.",
    portraitPath: "/assets/characters/willy_the_kid.png",
  },
  {
    name: "Сид Кетчум",
    description: "Сбросьте 2 карты, чтобы восстановить 1 ОЗ (в свой ход).",
    portraitPath: "/assets/characters/sid_ketchum.png",
  },
  {
    name: "Стервятник Сэм",
    description: "Когда игрок устранён, возьмите все его карты.",
    portraitPath: "/assets/characters/vulture_sam.png",
  },
  {
    name: "Педро Рамирес",
    description: "Первую карту берите из сброса.",
    portraitPath: "/assets/characters/pedro_ramirez.png",
  },
];

const rolesReference = [
  { name: "Шериф", color: "#f3b169", description: "Цель — устранить всех Бандитов и Ренегата. Роль открыта, +1 ОЗ." },
  { name: "Помощник", color: "#6bc46b", description: "Защищает Шерифа. Побеждает вместе с ним." },
  { name: "Бандит", color: "#e04040", description: "Цель — устранить Шерифа." },
  { name: "Ренегат", color: "#7a8fe0", description: "Цель — остаться последним. Должен убить всех, включая Шерифа." },
];

const roleDistribution = [
  { players: 2, roles: "1 Шериф, 1 Бандит" },
  { players: 3, roles: "1 Шериф, 1 Бандит, 1 Ренегат" },
  { players: 4, roles: "1 Шериф, 2 Бандита, 1 Ренегат" },
  { players: 5, roles: "1 Шериф, 1 Помощник, 2 Бандита, 1 Ренегат" },
  { players: 6, roles: "1 Шериф, 1 Помощник, 3 Бандита, 1 Ренегат" },
];

const roleLibrary = document.getElementById("roleLibrary");

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
      <strong>Распределение ролей</strong>
      <table class="role-table">
        ${roleDistribution.map((r) => `<tr><td>${r.players} игр.</td><td>${r.roles}</td></tr>`).join("")}
      </table>
    </div>
  `;
  roleLibrary.appendChild(distItem);
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

  const stateJson = JSON.stringify(state);
  if (stateJson === lastStateJson) {
    return;
  }
  lastStateJson = stateJson;

  currentState = state;
  const isSpectator = !!state.isSpectator;
  if (spectatorBanner) {
    spectatorBanner.classList.toggle("hidden", !isSpectator);
  }
  if (roomCodeBadge && state.roomCode) {
    roomCodeBadge.textContent = `Комната: ${state.roomCode}`;
    roomCodeBadge.classList.remove("hidden");
  }
  if (state.gameOver) {
    turnInfo.textContent = state.winnerMessage || "Игра окончена.";
  } else {
    turnInfo.textContent = state.started
      ? `Ход: ${state.currentPlayerName}`
      : "Ожидание начала игры...";
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
    eventLog.innerHTML = '<p class="hint">Событий пока нет.</p>';
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

  const tableEl = document.createElement("div");
  tableEl.className = "poker-table";
  playersContainer.appendChild(tableEl);

  const positions = computeTablePositions(state.players.length);

  state.players.forEach((player, index) => {
    const card = document.createElement("div");
    card.className = "player-card";
    if (player.id === state.currentPlayerId) {
      card.classList.add("active");
    }
    if (!player.isAlive) {
      card.classList.add("out");
    }
    if (index === 0 && !state.isSpectator) {
      card.classList.add("self");
    }

    const pos = positions[index];
    card.style.left = pos.left + "%";
    card.style.top = pos.top + "%";

    const portraitHtml = player.characterPortrait
      ? `<img class="player-portrait" src="${player.characterPortrait}" alt="${player.characterName}" onerror="this.style.display='none'"/>`
      : "";

    const equipHtml = player.equipment && player.equipment.length > 0
      ? player.equipment.map((e) => {
          const sv = e.suit ? ` ${formatSuitValue(e)}` : "";
          return `<span class="equip-tag">${e.name}${sv}</span>`;
        }).join(" ")
      : "";

    const distanceHtml = state.distances && state.distances[player.id] != null
      ? `<small class="distance-label">Дистанция: ${state.distances[player.id]}</small>`
      : "";

    const hostBadge = state.hostId === player.id ? '<span class="host-badge">Ведущий</span>' : "";

    card.innerHTML = `
      <div class="player-header">
        ${portraitHtml}
        <div>
          <strong>${player.name}</strong>${hostBadge}
          <p>${player.characterName}</p>
        </div>
      </div>
      <small>${player.characterDescription}</small>
      <p class="role-line">Роль: ${player.role}</p>
      <p>ОЗ: ${player.hp} / ${player.maxHp} | Карты: ${player.handCount}</p>
      ${equipHtml ? `<div class="equip-row">${equipHtml}</div>` : ""}
      ${distanceHtml}
    `;
    playersContainer.appendChild(card);
  });

  handCards.innerHTML = "";
  if (state.yourHand.length === 0) {
    handHint.textContent = "В руке нет карт. Завершите ход или дождитесь добора.";
  } else {
    handHint.textContent = "Нажмите на карту, чтобы сыграть.";
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
      element.dataset.tooltip = `Можно сыграть только ${state.bangLimit} Бэнг! за ход.`;
    }
    const imageHtml = card.imagePath
      ? `<img class="card-image" src="${card.imagePath}" alt="${card.name}" onerror="this.style.display='none'"/>`
      : "";
    const categoryTag = card.category === "Blue" || card.category === "Weapon" || card.category === "Brown"
      ? `<span class="tag equip">${cardCategoryLabels[card.category] || card.category}</span>`
      : "";
    const suitValueLabel = card.suit
      ? `<span class="suit-badge" style="color:${suitColors[card.suit] || '#fff'}">${formatSuitValue(card)}</span>`
      : "";
    element.innerHTML = `
      <div>
        ${imageHtml}
        <strong>${card.name} ${suitValueLabel}</strong>
        <small>${card.description}</small>
      </div>
      <div>
        <span class="tag">${cardTypeLabels[card.type] || card.type}</span>
        ${card.requiresTarget ? "<span class=\"tag\">Цель</span>" : ""}
        ${categoryTag}
      </div>
    `;
    element.addEventListener("click", () => onCardSelected(card, index, element));
    handCards.appendChild(element);
  });

  const hasPending = !!state.pendingAction;
  const isYourTurn = state.started && state.currentPlayerId === playerId;
  const isHost = state.hostId === playerId;
  endTurnButton.disabled = !isYourTurn || state.gameOver || hasPending || isSpectator;
  startButton.disabled = isSpectator || !isHost;
  if (newGameButton) {
    newGameButton.disabled = !isHost;
  }

  if (abilityButton) {
    const myChar = getMyCharacterName(state);
    const me = state.players.find((p) => p.id === playerId);
    const canUseAbility = !isSpectator && isYourTurn && !state.gameOver && !hasPending &&
      myChar === "Сид Кетчум" && state.yourHand.length >= 2 &&
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
        turnInfo.textContent = `Ожидание ответа от ${pa.respondingPlayerName}...`;
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
  targetPrompt.textContent = card.targetHint || "Выберите цель.";

  const effectiveType = card.type === "Missed" && getMyCharacterName(currentState) === "Каламити Джанет"
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
    targetPrompt.textContent = "Пока нет доступных целей. Попросите другого игрока присоединиться.";
    const empty = document.createElement("div");
    empty.className = "hint";
    empty.textContent = "Цели появятся, когда к столу присоединится другой игрок.";
    targetList.appendChild(empty);
  } else {
    availableTargets.forEach((player) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const distLabel = player.distance != null ? ` [дист: ${player.distance}]` : "";
      button.textContent = `${player.name} (${player.characterName})${distLabel}`;
      if (player.outOfRange) {
        button.disabled = true;
        button.title = "Вне дальности";
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
    responseTitle.textContent = type === "GeneralStorePick" ? "Магазин" : "Кит Карлсон";
    responsePass.classList.add("hidden");
    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `${card.name}${sv}`;
        button.addEventListener("click", () => respondToAction("play_card", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "DiscardExcess") {
    responseTitle.textContent = "Сброс";
    responsePass.classList.add("hidden");
    state.yourHand.forEach((card, idx) => {
      const button = document.createElement("button");
      button.className = "target-button";
      const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
      button.textContent = `${card.name}${sv}`;
      button.addEventListener("click", () => respondToAction("play_card", idx));
      responseCards.appendChild(button);
    });
  } else if (type === "ChooseStealSource") {
    responseTitle.textContent = "Выбор цели";
    responsePass.classList.add("hidden");

    const handButton = document.createElement("button");
    handButton.className = "target-button";
    handButton.textContent = "Случайная карта из руки";
    handButton.addEventListener("click", () => respondToAction("hand", null));
    responseCards.appendChild(handButton);

    if (pendingAction.revealedCards) {
      pendingAction.revealedCards.forEach((card, idx) => {
        const button = document.createElement("button");
        button.className = "target-button";
        const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
        button.textContent = `Снаряжение: ${card.name}${sv}`;
        button.addEventListener("click", () => respondToAction("equipment", idx));
        responseCards.appendChild(button);
      });
    }
  } else if (type === "JesseJonesSteal") {
    responseTitle.textContent = "Джесси Джонс";
    responsePass.classList.add("hidden");

    const targets = state.players.filter(
      (p) => p.id !== playerId && p.isAlive && p.handCount > 0
    );
    if (targets.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = "Нет игроков с картами для взятия.";
      responseCards.appendChild(hint);
    } else {
      targets.forEach((player) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `${player.name} (${player.handCount} карт)`;
        button.addEventListener("click", () => respondToAction("steal", null, player.id));
        responseCards.appendChild(button);
      });
    }
  } else {
    const isIndians = type === "IndiansDefense";
    const isDuel = type === "DuelChallenge";
    const myChar = getMyCharacterName(state);
    const isJanet = myChar === "Каламити Джанет";

    let requiredTypes;
    if (isIndians || isDuel) {
      requiredTypes = isJanet ? ["Bang", "Missed"] : ["Bang"];
    } else {
      requiredTypes = isJanet ? ["Missed", "Bang"] : ["Missed"];
    }
    const requiredName = requiredTypes.map((t) => t === "Bang" ? "Бэнг!" : "Мимо!").join("/");

    responseTitle.textContent = isDuel ? "Дуэль" : "Защита";
    responsePass.classList.remove("hidden");
    responsePass.textContent = isDuel ? "Сдаться" : "Принять удар";

    const validCards = state.yourHand
      .map((card, idx) => ({ card, idx }))
      .filter(({ card }) => requiredTypes.includes(card.type));

    if (validCards.length === 0) {
      const hint = document.createElement("div");
      hint.className = "hint";
      hint.textContent = `У вас нет карт ${requiredName} для ответа.`;
      responseCards.appendChild(hint);
    } else {
      validCards.forEach(({ card, idx }) => {
        const button = document.createElement("button");
        button.className = "target-button";
        button.textContent = `Сыграть ${card.name}`;
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
    setStatus("Начните игру перед тем, как играть карты.");
    return;
  }

  if (currentState.pendingAction) {
    setStatus("Ожидание ответа игрока.");
    return;
  }

  if (currentState.currentPlayerId !== playerId) {
    setStatus("Дождитесь своего хода, чтобы сыграть карту.");
    return;
  }

  const myChar = getMyCharacterName(currentState);
  const effectiveType = card.type === "Missed" && myChar === "Каламити Джанет" ? "Bang" : card.type;

  if (effectiveType === "Bang" && currentState.bangsPlayedThisTurn >= currentState.bangLimit) {
    showCardTooltip(element, `Можно сыграть только ${currentState.bangLimit} Бэнг! за ход.`);
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
    const sv = card.suit ? ` ${formatSuitValue(card)}` : "";
    button.textContent = `${card.name}${sv}`;
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
    throw new Error(payload.message || "Запрос не выполнен.");
  }

  return payload.data;
};

const enterLobby = () => {
  joinPanel.classList.add("hidden");
  if (lobbyPanel) lobbyPanel.classList.remove("hidden");
  gamePanel.classList.add("hidden");
  refreshRoomList();
  if (connection && connection.state === "Connected") {
    connection.invoke("JoinRoom", "lobby").catch(() => {});
  }
};

const joinGame = async () => {
  const name = playerNameInput.value.trim();
  if (!name) {
    setStatus("Введите имя, чтобы присоединиться.");
    return;
  }
  localStorage.setItem("bangPlayerName", name);
  enterLobby();
};

const createRoom = async () => {
  try {
    const data = await apiPost("/api/room/create", {});
    const code = data.roomCode;
    await joinRoom(code);
  } catch (error) {
    setStatus(error.message);
  }
};

const joinRoom = async (code) => {
  const name = localStorage.getItem("bangPlayerName") || playerNameInput.value.trim();
  if (!name) {
    setStatus("Сначала введите имя.");
    return;
  }
  try {
    const data = await apiPost("/api/join", { name, roomCode: code });
    playerId = data.playerId;
    roomCode = code;
    localStorage.setItem("bangPlayerId", playerId);
    localStorage.setItem("bangPlayerName", name);
    localStorage.setItem("bangRoomCode", code);
    if (lobbyPanel) lobbyPanel.classList.add("hidden");
    joinPanel.classList.add("hidden");
    gamePanel.classList.remove("hidden");
    setStatus(`Подключены как ${name}`);
    updateState(data.state);
    if (connection && connection.state === "Connected") {
      connection.invoke("LeaveRoom", "lobby").catch(() => {});
      connection.invoke("Register", playerId).catch(() => {});
      connection.invoke("JoinRoom", code).catch(() => {});
    }
  } catch (error) {
    setStatus(error.message);
  }
};

const leaveRoom = async () => {
  if (!playerId) return;
  const oldRoom = roomCode;
  try {
    await apiPost("/api/leave", { playerId });
  } catch {}
  if (connection && connection.state === "Connected" && oldRoom) {
    connection.invoke("LeaveRoom", oldRoom).catch(() => {});
  }
  playerId = null;
  roomCode = null;
  currentState = null;
  lastStateJson = null;
  localStorage.removeItem("bangPlayerId");
  localStorage.removeItem("bangRoomCode");
  gamePanel.classList.add("hidden");
  enterLobby();
  setStatus("Вы вышли из комнаты.");
};

const renamePlayer = async () => {
  if (!playerId) return;
  const currentName = localStorage.getItem("bangPlayerName") || "";
  const newName = prompt("Введите новое имя:", currentName);
  if (!newName || !newName.trim() || newName.trim() === currentName) return;
  try {
    await apiPost("/api/rename", { playerId, newName: newName.trim() });
    localStorage.setItem("bangPlayerName", newName.trim());
    setStatus(`Имя изменено на ${newName.trim()}`);
  } catch (error) {
    setStatus(error.message);
  }
};

const renderRoomList = (rooms) => {
  if (!roomListContainer) return;
  roomListContainer.innerHTML = "";
  if (!rooms || rooms.length === 0) {
    roomListContainer.innerHTML = '<p class="hint">Пока нет комнат. Создайте!</p>';
    return;
  }
  rooms.forEach((room) => {
    const item = document.createElement("div");
    item.className = "room-item";
    item.innerHTML = `
      <div>
        <strong class="room-code-badge">${room.roomCode}</strong>
        <span>${room.statusText}</span>
        <small>
          ${room.playerCount} ${formatCountLabel(room.playerCount, "игрок", "игрока", "игроков")},
          ${room.spectatorCount} ${formatCountLabel(room.spectatorCount, "зритель", "зрителя", "зрителей")}
        </small>
      </div>
      <button class="primary">Войти</button>
    `;
    item.querySelector("button").addEventListener("click", () => joinRoom(room.roomCode));
    roomListContainer.appendChild(item);
  });
};

const refreshRoomList = async () => {
  try {
    const response = await fetch("/api/rooms");
    const payload = await response.json();
    if (response.ok && payload.data) renderRoomList(payload.data);
  } catch {}
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
if (leaveButton) {
  leaveButton.addEventListener("click", leaveRoom);
}
if (renameButton) {
  renameButton.addEventListener("click", renamePlayer);
}
if (createRoomButton) {
  createRoomButton.addEventListener("click", createRoom);
}
if (joinRoomButton) {
  joinRoomButton.addEventListener("click", () => {
    const code = roomCodeInput ? roomCodeInput.value.trim().toUpperCase() : "";
    if (code) joinRoom(code);
    else setStatus("Введите код комнаты.");
  });
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

if (roomCodeInput) {
  roomCodeInput.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      const code = roomCodeInput.value.trim().toUpperCase();
      if (code) joinRoom(code);
    }
  });
}

chatInput.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    sendChat();
  }
});

renderLibrary();

const tryReconnect = async () => {
  const savedId = localStorage.getItem("bangPlayerId");
  const savedName = localStorage.getItem("bangPlayerName");
  const savedRoom = localStorage.getItem("bangRoomCode");
  if (!savedId) {
    if (savedName) enterLobby();
    return;
  }
  try {
    const response = await fetch(`/api/reconnect?playerId=${savedId}`);
    const payload = await response.json();
    if (response.ok && payload.data) {
      playerId = savedId;
      roomCode = savedRoom;
      joinPanel.classList.add("hidden");
      if (lobbyPanel) lobbyPanel.classList.add("hidden");
      gamePanel.classList.remove("hidden");
      setStatus(`Переподключены как ${savedName || "игрок"}`);
      updateState(payload.data);
      if (connection && connection.state === "Connected") {
        connection.invoke("Register", playerId).catch(() => {});
        if (roomCode) connection.invoke("JoinRoom", roomCode).catch(() => {});
      }
    } else {
      localStorage.removeItem("bangPlayerId");
      localStorage.removeItem("bangRoomCode");
      if (savedName) enterLobby();
    }
  } catch {
    localStorage.removeItem("bangPlayerId");
    localStorage.removeItem("bangRoomCode");
    if (savedName) enterLobby();
  }
};

const initSignalR = async () => {
  connection = new signalR.HubConnectionBuilder()
    .withUrl("/gamehub")
    .withAutomaticReconnect()
    .build();

  connection.on("StateUpdated", (state) => {
    updateState(state);
  });

  connection.on("RoomsUpdated", (rooms) => {
    renderRoomList(rooms);
  });

  connection.onreconnected(async () => {
    if (playerId) {
      await connection.invoke("Register", playerId).catch(() => {});
    }
    if (roomCode) {
      await connection.invoke("JoinRoom", roomCode).catch(() => {});
      try {
        const response = await fetch(`/api/reconnect?playerId=${playerId}`);
        const payload = await response.json();
        if (response.ok && payload.data) updateState(payload.data);
      } catch {}
    } else if (!lobbyPanel?.classList.contains("hidden")) {
      await connection.invoke("JoinRoom", "lobby").catch(() => {});
      refreshRoomList();
    }
  });

  try {
    await connection.start();
  } catch (err) {
    console.error("SignalR connection failed:", err);
  }
};

initSignalR();
tryReconnect();
