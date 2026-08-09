import rawCards from '../data/cards.json';

export interface CardInfo {
  id: number;
  itemId: string;
  type: 'creature' | 'spell' | 'charm';
  folder: string;
  token: boolean;
  commander: boolean;
  rarity: number;
  element: number;
  costType: 'gold' | 'mana' | 'health';
  cost: number;
  atk?: number;
  hp?: number;
  spd?: number;
  name: { ru: string; en: string };
  desc: { ru: string; en: string };
}

export const CARDS = rawCards as CardInfo[];

/** Карты, которые могут лежать в коллекции игрока (токены движковые, их не выдают). */
export const COLLECTIBLE = CARDS.filter(c => !c.token);

// Цвета — из палитры питча (Docs/pitch/chaos-commander-pitch.html).
// Названия здесь не держим: они языковые, лежат в i18n/strings (rarity / element).
export type RarityKey = 'common' | 'rare' | 'epic' | 'legendary' | 'exotic';
export type ElementKey = 'red' | 'blue' | 'green' | 'yellow' | 'white' | 'black';

export const RARITIES: { key: RarityKey; color: string }[] = [
  { key: 'common', color: '#a99fb0' },
  { key: 'rare', color: '#6fa65a' },
  { key: 'epic', color: '#8a6fc0' },
  { key: 'legendary', color: '#f2a33c' },
  { key: 'exotic', color: '#c24b33' },
];

export const ELEMENTS: { bit: number; key: ElementKey; color: string }[] = [
  { bit: 1, key: 'red', color: '#c24b33' },
  { bit: 2, key: 'blue', color: '#5a7fbe' },
  { bit: 4, key: 'green', color: '#6fa65a' },
  { bit: 8, key: 'yellow', color: '#f2a33c' },
  { bit: 16, key: 'white', color: '#ede6da' },
  { bit: 32, key: 'black', color: '#3a3145' },
];

export function elementsOf(mask: number) {
  return ELEMENTS.filter(e => (mask & e.bit) !== 0);
}

/** «Масть» типа карты: пики — существо, бубны — заклинание, черви — чары, звезда — командир. */
export function typeGlyph(card: CardInfo): string {
  if (card.commander) return '★';
  return { creature: '♠', spell: '♦', charm: '♥' }[card.type];
}

export const COST_COLOR: Record<CardInfo['costType'], string> = {
  gold: '#f2a33c',
  mana: '#5a7fbe',
  health: '#c24b33',
};
