import type { CSSProperties } from 'react';
import { CardInfo, COST_COLOR, elementsOf, RARITIES, typeGlyph } from '../lib/cards';
import { useLang } from '../i18n/lang';

/** *N* в описаниях — акцент (шаблонизатор игры), рендерим болдом. */
function DescText({ text }: { text: string }) {
  const parts = text.replace(/\\n/g, ' ').split(/\*([^*]+)\*/g);
  return (
    <>
      {parts.map((p, i) => (i % 2 === 1 ? <b key={i}>{p}</b> : <span key={i}>{p}</span>))}
    </>
  );
}

interface Props {
  card: CardInfo;
  /** undefined — витрина; 0 — не найдена (приглушаем); N>0 — штук в коллекции. */
  count?: number;
}

export default function CardFrame({ card, count }: Props) {
  const { lang, t } = useLang();
  const rarity = RARITIES[card.rarity] ?? RARITIES[0];
  const ghost = count === 0;
  const typeName = t.cardType[card.type];
  const title = `${t.rarity[rarity.key]} · ${typeName} · ${t.cost[card.costType]}: ${card.cost}`;

  return (
    <article
      className={'card-frame' + (ghost ? ' card--ghost' : '')}
      style={{ '--rarity': rarity.color } as CSSProperties}
      title={title}
    >
      <header className="card-top">
        <span
          className="card-cost"
          style={{ background: COST_COLOR[card.costType] }}
          aria-label={`${t.cost[card.costType]}: ${card.cost}`}
        >
          {card.cost}
        </span>
        <span className="card-type-glyph" aria-label={typeName}>
          {typeGlyph(card)}
        </span>
      </header>

      <h3 className="card-name">{card.name[lang]}</h3>
      <p className="card-name-en">{lang === 'ru' ? card.name.en : card.name.ru}</p>

      <div className="card-elems" aria-hidden="true">
        {elementsOf(card.element).map(e => (
          <i
            key={e.bit}
            className="elem-dot"
            style={{ background: e.color }}
            title={t.element[e.key]}
          />
        ))}
      </div>

      {card.desc[lang] && (
        <p className="card-desc">
          <DescText text={card.desc[lang]} />
        </p>
      )}

      {card.type === 'creature' ? (
        <div className="card-stats">
          <div className="stat">
            <b>{card.atk ?? 0}</b>
            <span>{t.card.atk}</span>
          </div>
          <div className="stat">
            <b>{card.hp ?? 0}</b>
            <span>{t.card.hp}</span>
          </div>
          <div className="stat">
            <b>{card.spd ?? 0}</b>
            <span>{t.card.spd}</span>
          </div>
        </div>
      ) : (
        <div className="card-kind">{typeName}</div>
      )}

      {count !== undefined && count > 0 && <span className="card-count">×{count}</span>}
      {ghost && <span className="card-missing">{t.card.notOwned}</span>}
    </article>
  );
}
