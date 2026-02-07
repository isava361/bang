import { suitSymbols } from "./constants.ts";

export const escapeHtml = (str: string): string => {
  const div = document.createElement("div");
  div.textContent = str;
  return div.innerHTML;
};

export const computeTablePositions = (count: number): Array<{ left: number; top: number }> => {
  const positions: Array<{ left: number; top: number }> = [];
  const hRadius = Math.min(42, 18 + count * 4);
  const vRadius = Math.min(35, 14 + count * 4);
  for (let i = 0; i < count; i++) {
    const angle = (-Math.PI / 2) + (2 * Math.PI * i) / count;
    positions.push({
      left: Math.max(14, Math.min(86, 50 + hRadius * Math.cos(angle))),
      top: Math.max(14, Math.min(86, 50 - vRadius * Math.sin(angle))),
    });
  }
  return positions;
};

export const formatCountLabel = (count: number, singular: string, few: string, many: string): string => {
  const mod10 = count % 10;
  const mod100 = count % 100;
  if (mod10 === 1 && mod100 !== 11) return singular;
  if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return few;
  return many;
};

export const formatCardValue = (value: number): string => {
  if (value === 11) return "J";
  if (value === 12) return "Q";
  if (value === 13) return "K";
  if (value === 14) return "A";
  return value.toString();
};

export const formatSuitValue = (card: { suit: string; value: number }): string => {
  if (!card.suit) return "";
  const sym = suitSymbols[card.suit] || "?";
  const val = formatCardValue(card.value);
  return `${val}${sym}`;
};
