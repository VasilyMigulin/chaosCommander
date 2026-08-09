// Извлекает данные карт из Unity-ассетов игры в src/data/cards.json.
// Запуск: npm run extract-cards (перезапускать после изменения ростера в Unity).
import { readFileSync, readdirSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, '..', '..');
const expansionDir = join(repoRoot, 'Assets', 'Resources', 'Expansion', 'Standard');
const csvPath = join(repoRoot, 'Assets', 'Localization', 'card_text.csv');
const outPath = resolve(__dirname, '..', 'src', 'data', 'cards.json');

const TYPE_BY_CLASS = {
  CardCreatureModel: 'creature',
  CardSpellModel: 'spell',
  CardCharmModel: 'charm',
};
const COST_TYPE = ['gold', 'mana', 'health']; // EnumService.ResourceType

// ── card_text.csv: key;ru;en (значения в кавычках, могут содержать ; и переводы строк) ──
function parseCsv(text) {
  const rows = [];
  let field = '', row = [], inQuotes = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inQuotes) {
      if (c === '"') {
        if (text[i + 1] === '"') { field += '"'; i++; }
        else inQuotes = false;
      } else field += c;
    } else if (c === '"') inQuotes = true;
    else if (c === ';') { row.push(field); field = ''; }
    else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(field); field = '';
      if (row.length > 1 || row[0] !== '') rows.push(row);
      row = [];
    } else field += c;
  }
  if (field !== '' || row.length) { row.push(field); rows.push(row); }
  return rows;
}

const csvText = readFileSync(csvPath, 'utf8').replace(/^﻿/, '');
const loc = new Map(); // key → {ru,en}
for (const [key, ru, en] of parseCsv(csvText).slice(1)) {
  if (key) loc.set(key, { ru: ru ?? '', en: en ?? '' });
}

// ── Ассеты: берём data-блок модели карты, поля на отступе ровно 8 пробелов ──
function intField(block, name) {
  const m = block.match(new RegExp(`^ {8}${name}: (-?\\d+)\\s*$`, 'm'));
  return m ? parseInt(m[1], 10) : undefined;
}

const cards = [];
const warnings = [];
for (const folder of ['creature', 'spell', 'charm', 'token']) {
  let files;
  try { files = readdirSync(join(expansionDir, folder)); } catch { continue; }
  for (const file of files.filter(f => f.endsWith('.asset'))) {
    const text = readFileSync(join(expansionDir, folder, file), 'utf8');
    const m = text.match(
      /type: \{class: (CardCreatureModel|CardSpellModel|CardCharmModel),[^}]*\}\r?\n\s*data:\r?\n((?: {8}.*\r?\n?)+)/
    );
    if (!m) { warnings.push(`нет модели: ${folder}/${file}`); continue; }
    const [, cls, block] = m;
    const id = intField(block, 'Id');
    if (id === undefined) { warnings.push(`нет Id: ${folder}/${file}`); continue; }
    const names = loc.get(`card.standard.${id}.name`);
    const descs = loc.get(`card.standard.${id}.desc`);
    if (!names) warnings.push(`нет локализации имени: ${folder}/${file} (id ${id})`);
    cards.push({
      id,
      itemId: `standard_${id}`,
      type: TYPE_BY_CLASS[cls],
      folder,
      token: intField(block, 'IsToken') === 1,
      commander: intField(block, 'IsCommander') === 1,
      rarity: intField(block, 'Rarity') ?? 0,
      element: intField(block, 'Element') ?? 0,
      costType: COST_TYPE[intField(block, 'PlayCost') ?? 0],
      cost: intField(block, 'PlayCostAmount') ?? 0,
      atk: intField(block, 'Attack'),
      hp: intField(block, 'MaxHealth'),
      spd: intField(block, 'Speed'),
      name: names ?? { ru: `#${id}`, en: `#${id}` },
      desc: descs ?? { ru: '', en: '' },
    });
  }
}

cards.sort((a, b) => a.id - b.id);
const seen = new Set();
for (const c of cards) {
  if (seen.has(c.id)) warnings.push(`дубль id ${c.id}`);
  seen.add(c.id);
}

mkdirSync(dirname(outPath), { recursive: true });
writeFileSync(outPath, JSON.stringify(cards, null, 1), 'utf8');

const byType = {};
for (const c of cards) byType[c.folder] = (byType[c.folder] ?? 0) + 1;
console.log(`Карт: ${cards.length}`, byType);
if (warnings.length) console.log('Предупреждения:\n  ' + warnings.join('\n  '));
