// Тонкий клиент PlayFab Client API (title 180E80) поверх fetch.
// На сайте живут только публичные клиентские вызовы — никаких секретных ключей.

const TITLE_ID = '180E80';
const BASE = `https://${TITLE_ID.toLowerCase()}.playfabapi.com`;

export interface Session {
  ticket: string;
  playFabId: string;
  savedAt: number;
}

export interface InventoryItem {
  ItemId: string;
  RemainingUses?: number;
  DisplayName?: string;
}

export interface InventoryData {
  Inventory: InventoryItem[];
  VirtualCurrency?: Record<string, number>;
}

/** Контракт CloudScript-функции GetProfile (BackendModels.PlayerProfileData). */
export interface ProfileData {
  Name?: string;
  Rank?: string;
  Mmr?: number;
  Level?: number;
  Wins?: number;
  Losses?: number;
  GamesPlayed?: number;
  AchievementsEarned?: number;
  AchievementsTotal?: number;
  BoostersOpened?: number;
  CardsCollected?: number;
}

export class PlayFabError extends Error {
  code?: string;
  constructor(message: string, code?: string) {
    super(message);
    this.code = code;
  }
}

const ERROR_RU: Record<string, string> = {
  AccountNotFound: 'Аккаунт не найден. Проверьте почту или зарегистрируйтесь.',
  InvalidEmailOrPassword: 'Неверная почта или пароль.',
  InvalidUsernameOrPassword: 'Неверная почта или пароль.',
  InvalidParams: 'Проверьте заполнение полей.',
  AccountBanned: 'Аккаунт заблокирован.',
  InvalidSessionTicket: 'Сессия истекла — войдите заново.',
  ExpiredSessionTicket: 'Сессия истекла — войдите заново.',
  EmailAddressNotAvailable: 'На эту почту аккаунт уже зарегистрирован.',
  UsernameNotAvailable: 'Такой ник уже занят — придумайте другой.',
  NameNotAvailable: 'Такой ник уже занят — придумайте другой.',
  InvalidEmailAddress: 'Проверьте адрес почты.',
  InvalidPassword: 'Пароль должен быть от 6 до 100 символов.',
  InvalidUsername: 'Ник должен быть от 3 до 20 символов, без пробелов.',
};

async function call<T>(path: string, body: object, ticket?: string): Promise<T> {
  let res: Response;
  try {
    res = await fetch(BASE + path, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(ticket ? { 'X-Authorization': ticket } : {}),
      },
      body: JSON.stringify(body),
    });
  } catch {
    throw new PlayFabError('Сервер недоступен. Проверьте соединение.');
  }
  const json = await res.json().catch(() => null);
  if (!res.ok || !json || json.code !== 200) {
    const code: string | undefined = json?.error;
    throw new PlayFabError(
      (code && ERROR_RU[code]) || json?.errorMessage || `Ошибка сервера (HTTP ${res.status})`,
      code
    );
  }
  return json.data as T;
}

interface LoginData {
  SessionTicket: string;
  PlayFabId: string;
}

function toSession(data: LoginData): Session {
  return { ticket: data.SessionTicket, playFabId: data.PlayFabId, savedAt: Date.now() };
}

export async function loginWithEmail(email: string, password: string): Promise<Session> {
  const data = await call<LoginData>('/Client/LoginWithEmailAddress', {
    TitleId: TITLE_ID,
    Email: email,
    Password: password,
  });
  return toSession(data);
}

/**
 * Регистрация по почте. Ник задаём сразу через DisplayName — тогда игра при первом
 * входе не переспросит его на экране «Давай знакомиться».
 */
export async function registerWithEmail(
  email: string,
  password: string,
  displayName: string
): Promise<Session> {
  const data = await call<LoginData>('/Client/RegisterPlayFabUser', {
    TitleId: TITLE_ID,
    Email: email,
    Password: password,
    DisplayName: displayName,
    RequireBothUsernameAndEmail: false,
  });
  return toSession(data);
}

/**
 * Вход/регистрация через Google. serverAuthCode — OAuth-код из Google Identity Services;
 * PlayFab обменивает его сам (client secret хранится в Game Manager, а не здесь).
 * Тот же способ, что и на Android, — значит аккаунт получается один и тот же.
 */
export async function loginWithGoogle(serverAuthCode: string): Promise<Session> {
  const data = await call<LoginData>('/Client/LoginWithGoogleAccount', {
    TitleId: TITLE_ID,
    ServerAuthCode: serverAuthCode,
    CreateAccount: true,
  });
  return toSession(data);
}

/** Вход/регистрация через Apple. identityToken — id_token из Sign in with Apple JS. */
export async function loginWithApple(identityToken: string): Promise<Session> {
  const data = await call<LoginData>('/Client/LoginWithApple', {
    TitleId: TITLE_ID,
    IdentityToken: identityToken,
    CreateAccount: true,
  });
  return toSession(data);
}

/** Вход гостевым ID устройства (тем же, каким игра делает тихий вход). Аккаунт НЕ создаём. */
export async function loginWithCustomId(customId: string): Promise<Session> {
  const data = await call<LoginData>('/Client/LoginWithCustomID', {
    TitleId: TITLE_ID,
    CustomId: customId,
    CreateAccount: false,
  });
  return toSession(data);
}

/** Задать ник. Нужен после соцвхода: там DisplayName не приходит сам. */
export function setDisplayName(session: Session, displayName: string): Promise<unknown> {
  return call('/Client/UpdateUserTitleDisplayName', { DisplayName: displayName }, session.ticket);
}

/** Ник текущего игрока; null — если ещё не задан. */
export async function getDisplayName(session: Session): Promise<string | null> {
  try {
    const data = await call<{ AccountInfo?: { TitleInfo?: { DisplayName?: string } } }>(
      '/Client/GetAccountInfo',
      {},
      session.ticket
    );
    return data.AccountInfo?.TitleInfo?.DisplayName || null;
  } catch {
    return null;
  }
}

export function getInventory(session: Session): Promise<InventoryData> {
  return call<InventoryData>('/Client/GetUserInventory', {}, session.ticket);
}

/** Профиль через classic CloudScript GetProfile; null — если функция не отвечает. */
export async function getProfile(session: Session): Promise<ProfileData | null> {
  try {
    const data = await call<{ FunctionResult?: ProfileData; Error?: unknown }>(
      '/Client/ExecuteCloudScript',
      { FunctionName: 'GetProfile' },
      session.ticket
    );
    if (data.Error || !data.FunctionResult || typeof data.FunctionResult !== 'object') return null;
    return data.FunctionResult;
  } catch {
    return null;
  }
}

// ── Хранение сессии ──────────────────────────────────────────────────────────
const STORE_KEY = 'shh.session';
const TICKET_TTL_MS = 20 * 60 * 60 * 1000; // тикет живёт ~24 ч, перестраховываемся

export function loadSession(): Session | null {
  try {
    const raw = localStorage.getItem(STORE_KEY);
    if (!raw) return null;
    const s = JSON.parse(raw) as Session;
    if (!s.ticket || Date.now() - s.savedAt > TICKET_TTL_MS) return null;
    return s;
  } catch {
    return null;
  }
}

export function saveSession(s: Session) {
  localStorage.setItem(STORE_KEY, JSON.stringify(s));
}

export function clearSession() {
  localStorage.removeItem(STORE_KEY);
}
