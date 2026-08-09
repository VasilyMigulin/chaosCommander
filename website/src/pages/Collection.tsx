import { CSSProperties, useEffect, useMemo, useState } from 'react';
import AuthCard from '../components/AuthCard';
import CardFrame from '../components/CardFrame';
import DownloadButton from '../components/DownloadButton';
import { CardInfo, COLLECTIBLE, RARITIES } from '../lib/cards';
import {
  clearSession,
  getInventory,
  getProfile,
  InventoryData,
  loadSession,
  PlayFabError,
  ProfileData,
  Session,
} from '../lib/playfab';
import { useLang } from '../i18n/lang';

const CURRENCIES: { code: 'GD' | 'GM' | 'SC'; color: string }[] = [
  { code: 'GD', color: '#f2a33c' },
  { code: 'GM', color: '#8a6fc0' },
  { code: 'SC', color: '#a99fb0' },
];

type TypeFilter = 'all' | CardInfo['type'];

export default function Collection() {
  const { t } = useLang();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [inventory, setInventory] = useState<InventoryData | null>(null);
  const [profile, setProfile] = useState<ProfileData | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [typeFilter, setTypeFilter] = useState<TypeFilter>('all');
  const [rarityFilter, setRarityFilter] = useState<number>(-1);
  const [ownedOnly, setOwnedOnly] = useState(false);

  useEffect(() => {
    if (!session) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    Promise.all([getInventory(session), getProfile(session)])
      .then(([inv, prof]) => {
        if (cancelled) return;
        setInventory(inv);
        setProfile(prof);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        if (
          err instanceof PlayFabError &&
          (err.code === 'InvalidSessionTicket' || err.code === 'ExpiredSessionTicket')
        ) {
          clearSession();
          setSession(null);
        } else {
          setError(err instanceof PlayFabError ? err.message : t.collection.loadError);
        }
      })
      .finally(() => !cancelled && setLoading(false));
    return () => {
      cancelled = true;
    };
  }, [session, t]);

  const counts = useMemo(() => {
    const map = new Map<string, number>();
    for (const item of inventory?.Inventory ?? []) {
      if (!item.ItemId) continue;
      map.set(item.ItemId, (map.get(item.ItemId) ?? 0) + (item.RemainingUses ?? 1));
    }
    return map;
  }, [inventory]);

  const ownedIds = useMemo(
    () => COLLECTIBLE.filter(c => (counts.get(c.itemId) ?? 0) > 0).length,
    [counts]
  );

  const shown = useMemo(() => {
    const q = search.trim().toLowerCase();
    return COLLECTIBLE.filter(c => {
      if (typeFilter !== 'all' && c.type !== typeFilter) return false;
      if (rarityFilter >= 0 && c.rarity !== rarityFilter) return false;
      if (ownedOnly && (counts.get(c.itemId) ?? 0) === 0) return false;
      if (q && !c.name.ru.toLowerCase().includes(q) && !c.name.en.toLowerCase().includes(q))
        return false;
      return true;
    }).sort((a, b) => a.cost - b.cost || a.id - b.id);
  }, [search, typeFilter, rarityFilter, ownedOnly, counts]);

  function logout() {
    clearSession();
    setSession(null);
    setInventory(null);
    setProfile(null);
  }

  if (!session) {
    return (
      <main className="wrap collection">
        <AuthCard onLogin={setSession} />
      </main>
    );
  }

  const wallet = inventory?.VirtualCurrency ?? {};
  const gamesPlayed = profile?.GamesPlayed ?? 0;
  const winRate = gamesPlayed > 0 ? Math.round(((profile?.Wins ?? 0) * 100) / gamesPlayed) : 0;
  const empty = !loading && !error && ownedIds === 0;

  const typeFilters: [TypeFilter, string][] = [
    ['all', t.collection.filterAll],
    ['creature', t.cardType.creature],
    ['spell', t.cardType.spell],
    ['charm', t.cardType.charm],
  ];

  return (
    <main className="wrap collection">
      <div className="coll-head">
        <div>
          <p className="eyebrow">{t.collection.eyebrow}</p>
          <h1 className="section-title">
            {profile?.Name || `${t.collection.playerFallback} ${session.playFabId.slice(0, 8)}`}
          </h1>
          <p className="coll-sub">
            {t.collection.collectedPre}
            <b>{ownedIds}</b>
            {t.collection.collectedMid}
            <b>{COLLECTIBLE.length}</b>
            {t.collection.collectedPost}
          </p>
        </div>
        <button className="btn btn-ghost btn-small" onClick={logout}>
          {t.collection.signOut}
        </button>
      </div>

      <div className="wallet">
        {CURRENCIES.map(c => (
          <span className="wallet-chip" key={c.code}>
            <i className="elem-dot" style={{ background: c.color }} aria-hidden="true" />
            {t.collection.currencies[c.code]}: <b>{wallet[c.code] ?? 0}</b>
          </span>
        ))}
      </div>

      {profile && gamesPlayed > 0 && (
        <div className="profile-strip">
          <div className="pstat">
            <b>{profile.Level ?? 1}</b>
            <span>{t.collection.stats.level}</span>
          </div>
          <div className="pstat">
            <b>
              {profile.Wins ?? 0}–{profile.Losses ?? 0}
            </b>
            <span>{t.collection.stats.record}</span>
          </div>
          <div className="pstat">
            <b>{winRate}%</b>
            <span>{t.collection.stats.winRate}</span>
          </div>
          <div className="pstat">
            <b>{profile.BoostersOpened ?? 0}</b>
            <span>{t.collection.stats.boosters}</span>
          </div>
        </div>
      )}

      {empty && (
        <div className="empty-nudge">
          <p>
            {t.collection.emptyPre}
            <b>{t.collection.emptyBold}</b>
            {t.collection.emptyPost}
          </p>
          <DownloadButton />
        </div>
      )}

      <div className="filters">
        <input
          type="search"
          className="search"
          placeholder={t.collection.searchPlaceholder}
          value={search}
          onChange={e => setSearch(e.target.value)}
          aria-label={t.collection.searchLabel}
        />
        <div className="chip-row" role="group" aria-label={t.collection.filterTypeLabel}>
          {typeFilters.map(([key, label]) => (
            <button
              key={key}
              className="chip-btn"
              aria-pressed={typeFilter === key}
              onClick={() => setTypeFilter(key)}
            >
              {label}
            </button>
          ))}
        </div>
        <div className="chip-row" role="group" aria-label={t.collection.filterRarityLabel}>
          <button
            className="chip-btn"
            aria-pressed={rarityFilter === -1}
            onClick={() => setRarityFilter(-1)}
          >
            {t.collection.anyRarity}
          </button>
          {RARITIES.map((r, i) => (
            <button
              key={r.key}
              className="chip-btn"
              aria-pressed={rarityFilter === i}
              style={{ '--chip-accent': r.color } as CSSProperties}
              onClick={() => setRarityFilter(i)}
            >
              {t.rarityPlural[r.key]}
            </button>
          ))}
        </div>
        <label className="owned-toggle">
          <input
            type="checkbox"
            checked={ownedOnly}
            onChange={e => setOwnedOnly(e.target.checked)}
          />
          {t.collection.ownedOnly}
        </label>
      </div>

      {loading && <p className="status-line">{t.collection.loading}</p>}
      {error && <p className="form-error">{error}</p>}

      {!loading && !error && (
        <>
          <div className="cards-row">
            {shown.map(c => (
              <CardFrame key={c.id} card={c} count={counts.get(c.itemId) ?? 0} />
            ))}
          </div>
          {shown.length === 0 && <p className="status-line">{t.collection.nothing}</p>}
        </>
      )}
    </main>
  );
}
