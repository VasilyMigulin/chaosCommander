import { Link } from 'react-router-dom';
import CardFrame from '../components/CardFrame';
import DownloadButton from '../components/DownloadButton';
import { CARDS } from '../lib/cards';
import { useLang } from '../i18n/lang';

const VILLAIN_IDS = [8, 103, 47, 99, 3, 7];
const FEATURE_GLYPHS = ['♠', '♥', '♦', '♣'];

// Бегущая строка — настоящие карты из ростера, по id: имя само встаёт на нужном языке.
const TICKER_IDS = [71, 124, 158, 57, 137, 145, 128, 96, 94, 66, 107, 153, 100, 53, 126, 60];

export default function Landing() {
  const { lang, t } = useLang();

  const villains = VILLAIN_IDS.map(id => CARDS.find(c => c.id === id)).filter(
    (c): c is NonNullable<typeof c> => Boolean(c)
  );
  const ticker = TICKER_IDS.map(id => CARDS.find(c => c.id === id)?.name[lang]).filter(Boolean);

  return (
    <main>
      <section className="hero halftone">
        <div className="hero-rays" aria-hidden="true" />
        <div className="wrap">
          <p className="hero-suits" aria-hidden="true">
            ♠ ♥ ♦ ♣
          </p>
          <p className="eyebrow">{t.hero.eyebrow}</p>
          <div className="hero-title-box">
            <h1 className={'hero-title' + (lang === 'en' ? ' is-en' : '')}>{t.hero.title}</h1>
            <span className="sticker sticker-bu" aria-hidden="true">
              {t.hero.sticker}
            </span>
          </div>
          <p className="hero-sub">{t.hero.sub}</p>
          <p className="hero-tag">
            {t.hero.tagPre}
            <b>{t.hero.tagBold}</b>
            {t.hero.tagPost}
          </p>
          <div className="chips">
            {t.hero.chips.map(c => (
              <span className="chip" key={c}>
                {c}
              </span>
            ))}
          </div>
          <div className="cta-row">
            <DownloadButton />
            <Link to="/collection" className="btn btn-ghost">
              {t.hero.cta}
            </Link>
          </div>
        </div>
      </section>

      <div className="ticker" aria-hidden="true">
        <div className="ticker-track">
          {[...ticker, ...ticker].map((name, i) => (
            <span className="ticker-item" key={i}>
              {name}
            </span>
          ))}
        </div>
      </div>

      <section className="section" id="game">
        <div className="wrap">
          <p className="eyebrow">{t.game.eyebrow}</p>
          <h2 className="section-title">{t.game.title}</h2>
          <p className="section-lead">
            {t.game.leadPre}
            <b>{t.game.leadBold}</b>
            {t.game.leadPost}
          </p>
          <div className="features-grid">
            {t.game.features.map((f, i) => (
              <div className="feature" key={f.title}>
                <span className="feature-glyph" aria-hidden="true">
                  {FEATURE_GLYPHS[i]}
                </span>
                <h3>{f.title}</h3>
                <p>{f.text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <div className="hazard-rule" aria-hidden="true" />

      <section className="section section-alt" id="campaign">
        <div className="wrap">
          <p className="eyebrow">{t.campaign.eyebrow}</p>
          <h2 className="section-title">{t.campaign.title}</h2>
          <p className="section-lead">
            {t.campaign.leadPre}
            <b>{t.campaign.leadBold}</b>
            {t.campaign.leadPost}
          </p>
          <ol className="campaign-list">
            {t.campaign.fights.map((f, i) => (
              <li className="fight" key={f.loc}>
                <span className="fight-num" aria-hidden="true">
                  {i + 1}
                </span>
                <div>
                  <p className="fight-boss">{f.boss}</p>
                  <h3 className="fight-loc">{f.loc}</h3>
                  <p className="fight-line">{f.line}</p>
                </div>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <div className="hazard-rule hazard-rule-red" aria-hidden="true" />

      <section className="section" id="villains">
        <div className="wrap">
          <p className="eyebrow">{t.villains.eyebrow}</p>
          <h2 className="section-title st-violet">{t.villains.title}</h2>
          <p className="section-lead">{t.villains.lead}</p>
          <div className="cards-row cards-showcase">
            {villains.map(c => (
              <CardFrame key={c.id} card={c} />
            ))}
          </div>
        </div>
      </section>

      <section className="section section-alt">
        <div className="wrap">
          <p className="eyebrow">{t.modes.eyebrow}</p>
          <h2 className="section-title st-green">{t.modes.title}</h2>
          <div className="modes-grid">
            {t.modes.items.map(m => (
              <div className="mode" key={m.title}>
                <h3>{m.title}</h3>
                <p>{m.text}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="section" id="expansion">
        <div className="wrap">
          <div className="addon-panel halftone">
            <span className="sticker sticker-soon" aria-hidden="true">
              {t.addon.sticker}
            </span>
            <div className="addon-copy">
              <p className="eyebrow">{t.addon.eyebrow}</p>
              <h2 className="section-title">{t.addon.title}</h2>
              <p className="section-lead">{t.addon.lead}</p>
            </div>
            <img
              className="addon-mark"
              src="/eggplanos.png"
              width={620}
              height={924}
              alt=""
              loading="lazy"
            />
          </div>
        </div>
      </section>
    </main>
  );
}
