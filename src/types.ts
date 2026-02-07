export interface CardView {
  name: string;
  type: string;
  category: string;
  description: string;
  requiresTarget: boolean;
  targetHint: string | null;
  imagePath: string;
  suit: string;
  value: number;
  isFresh?: boolean;
}

export interface PlayerView {
  id: string;
  name: string;
  hp: number;
  maxHp: number;
  isAlive: boolean;
  role: string;
  roleRevealed: boolean;
  characterName: string;
  characterDescription: string;
  characterPortrait: string;
  handCount: number;
  equipment: CardView[];
  revealedHand?: CardView[] | null;
}

export interface PendingActionView {
  type: string;
  respondingPlayerId: string;
  respondingPlayerName: string;
  message: string;
  revealedCards: CardView[] | null;
}

export interface GameStateView {
  started: boolean;
  currentPlayerId: string;
  currentPlayerName: string;
  gameOver: boolean;
  winnerMessage: string | null;
  players: PlayerView[];
  yourHand: CardView[];
  bangsPlayedThisTurn: number;
  bangLimit: number;
  eventLog: string[];
  chatMessages: string[];
  pendingAction: PendingActionView | null;
  weaponRange: number;
  distances: Record<string, number> | null;
  isSpectator?: boolean;
  roomCode?: string | null;
  hostId?: string | null;
  yourPublicId?: string | null;
  settings?: GameSettings | null;
  currentEventName?: string | null;
  currentEventDescription?: string | null;
}

export interface GameSettings {
  dodgeCity: boolean;
  highNoon: boolean;
  fistfulOfCards: boolean;
}

export interface RoomInfo {
  roomCode: string;
  playerCount: number;
  spectatorCount: number;
  started: boolean;
  gameOver: boolean;
  statusText: string;
}

export interface CardReference {
  name: string;
  type: string;
  category: string;
  description: string;
  imagePath: string;
}

export interface CharacterReference {
  name: string;
  description: string;
  portraitPath: string;
}

export interface RoleReference {
  name: string;
  color: string;
  description: string;
}

export interface RoleDistributionEntry {
  players: number;
  roles: string;
}

export interface SelectedCard {
  card: CardView;
  index: number;
}
