/**
 * Мост к Google Identity Services и Sign in with Apple JS.
 * Обе библиотеки грузятся лениво — только когда игрок реально нажал кнопку,
 * чтобы страница не тянула чужие скрипты просто так.
 */
import { APPLE_REDIRECT_URI, APPLE_SERVICES_ID, GOOGLE_CLIENT_ID } from './authConfig';

export class SocialAuthError extends Error {}

/** Игрок закрыл окно провайдера — это не ошибка, показывать её не нужно. */
export class SocialAuthCancelled extends SocialAuthError {}

export const googleEnabled = () => GOOGLE_CLIENT_ID.length > 0;
export const appleEnabled = () => APPLE_SERVICES_ID.length > 0;

const loaded = new Map<string, Promise<void>>();

function loadScript(src: string): Promise<void> {
  let p = loaded.get(src);
  if (!p) {
    p = new Promise<void>((resolve, reject) => {
      const el = document.createElement('script');
      el.src = src;
      el.async = true;
      el.onload = () => resolve();
      el.onerror = () => {
        loaded.delete(src);
        reject(new SocialAuthError('Не удалось загрузить скрипт входа. Проверьте соединение.'));
      };
      document.head.appendChild(el);
    });
    loaded.set(src, p);
  }
  return p;
}

// ── Google ───────────────────────────────────────────────────────────────────
// initCodeClient в popup-режиме отдаёт OAuth-код (аналог grantOfflineAccess),
// который PlayFab обменивает на своей стороне. Это тот же тип кода, что даёт
// Android, поэтому веб и телефон попадают в один и тот же аккаунт.

interface GoogleCodeResponse {
  code?: string;
  error?: string;
}

interface GoogleCodeClient {
  requestCode(): void;
}

interface GoogleNamespace {
  accounts: {
    oauth2: {
      initCodeClient(config: {
        client_id: string;
        scope: string;
        ux_mode: 'popup';
        callback: (r: GoogleCodeResponse) => void;
        error_callback?: (e: { type?: string }) => void;
      }): GoogleCodeClient;
    };
  };
}

declare global {
  interface Window {
    google?: GoogleNamespace;
    AppleID?: {
      auth: {
        init(config: {
          clientId: string;
          scope: string;
          redirectURI: string;
          usePopup: boolean;
        }): void;
        signIn(): Promise<{ authorization?: { id_token?: string } }>;
      };
    };
  }
}

/** Открывает окно Google и возвращает serverAuthCode для PlayFab. */
export async function googleAuthCode(): Promise<string> {
  if (!googleEnabled()) throw new SocialAuthError('Вход через Google ещё не настроен.');
  await loadScript('https://accounts.google.com/gsi/client');
  const google = window.google;
  if (!google) throw new SocialAuthError('Библиотека Google не загрузилась.');

  return new Promise<string>((resolve, reject) => {
    const client = google.accounts.oauth2.initCodeClient({
      client_id: GOOGLE_CLIENT_ID,
      scope: 'openid email profile',
      ux_mode: 'popup',
      callback: response => {
        if (response.code) resolve(response.code);
        else reject(new SocialAuthError('Google не выдал код авторизации.'));
      },
      error_callback: e => {
        const type = e?.type ?? '';
        if (type === 'popup_closed' || type === 'popup_failed_to_open')
          reject(new SocialAuthCancelled('cancelled'));
        else reject(new SocialAuthError('Google отказал во входе.'));
      },
    });
    client.requestCode();
  });
}

// ── Apple ────────────────────────────────────────────────────────────────────

/** Открывает окно Apple и возвращает identityToken (id_token) для PlayFab. */
export async function appleIdentityToken(): Promise<string> {
  if (!appleEnabled()) throw new SocialAuthError('Вход через Apple ещё не настроен.');
  await loadScript(
    'https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/en_US/appleid.auth.js'
  );
  const appleId = window.AppleID;
  if (!appleId) throw new SocialAuthError('Библиотека Apple не загрузилась.');

  appleId.auth.init({
    clientId: APPLE_SERVICES_ID,
    scope: 'name email',
    redirectURI: APPLE_REDIRECT_URI || window.location.origin,
    usePopup: true,
  });

  try {
    const res = await appleId.auth.signIn();
    const token = res?.authorization?.id_token;
    if (!token) throw new SocialAuthError('Apple не выдал токен.');
    return token;
  } catch (e) {
    // Apple кидает {error: 'popup_closed_by_user'} при закрытии окна.
    const err = e as { error?: string };
    if (err?.error === 'popup_closed_by_user') throw new SocialAuthCancelled('cancelled');
    if (e instanceof SocialAuthError) throw e;
    throw new SocialAuthError('Apple отказал во входе.');
  }
}
