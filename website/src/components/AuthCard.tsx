import { FormEvent, useState } from 'react';
import {
  loginWithApple,
  loginWithCustomId,
  loginWithEmail,
  loginWithGoogle,
  PlayFabError,
  registerWithEmail,
  saveSession,
  Session,
} from '../lib/playfab';
import {
  appleEnabled,
  appleIdentityToken,
  googleAuthCode,
  googleEnabled,
  SocialAuthCancelled,
  SocialAuthError,
} from '../lib/socialAuth';
import { useLang } from '../i18n/lang';

type Mode = 'login' | 'register';

export default function AuthCard({ onLogin }: { onLogin: (s: Session) => void }) {
  const { t } = useLang();
  const [mode, setMode] = useState<Mode>('login');
  const [guestMode, setGuestMode] = useState(false);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [nickname, setNickname] = useState('');
  const [customId, setCustomId] = useState('');

  const [busy, setBusy] = useState<null | 'email' | 'google' | 'apple'>(null);
  const [error, setError] = useState<string | null>(null);

  async function run(kind: 'email' | 'google' | 'apple', action: () => Promise<Session>) {
    setBusy(kind);
    setError(null);
    try {
      const session = await action();
      saveSession(session);
      onLogin(session);
    } catch (err) {
      if (err instanceof SocialAuthCancelled) return; // игрок сам закрыл окно
      if (err instanceof PlayFabError || err instanceof SocialAuthError) setError(err.message);
      else setError(t.auth.genericError);
    } finally {
      setBusy(null);
    }
  }

  function submitEmail(e: FormEvent) {
    e.preventDefault();
    if (guestMode) {
      run('email', () => loginWithCustomId(customId.trim()));
    } else if (mode === 'login') {
      run('email', () => loginWithEmail(email.trim(), password));
    } else {
      run('email', () => registerWithEmail(email.trim(), password, nickname.trim()));
    }
  }

  function switchMode(next: Mode) {
    setMode(next);
    setGuestMode(false);
    setError(null);
  }

  const anyBusy = busy !== null;
  const login = mode === 'login';

  return (
    <div className="login-card">
      <p className="eyebrow">{login ? t.auth.eyebrowLogin : t.auth.eyebrowRegister}</p>
      <h1 className="section-title">{login ? t.auth.titleLogin : t.auth.titleRegister}</h1>

      <div className="tabs" role="tablist">
        <button
          role="tab"
          aria-selected={login}
          className={login ? 'tab tab-on' : 'tab'}
          onClick={() => switchMode('login')}
        >
          {t.auth.tabLogin}
        </button>
        <button
          role="tab"
          aria-selected={!login}
          className={!login ? 'tab tab-on' : 'tab'}
          onClick={() => switchMode('register')}
        >
          {t.auth.tabRegister}
        </button>
      </div>

      <p className="login-hint">{login ? t.auth.hintLogin : t.auth.hintRegister}</p>

      <div className="social-row">
        <button
          type="button"
          className="social-btn"
          disabled={!googleEnabled() || anyBusy}
          title={googleEnabled() ? undefined : t.auth.googleOff}
          onClick={() => run('google', async () => loginWithGoogle(await googleAuthCode()))}
        >
          <span className="social-glyph" aria-hidden="true">
            G
          </span>
          {busy === 'google' ? t.auth.googleBusy : t.auth.google}
        </button>
        <button
          type="button"
          className="social-btn"
          disabled={!appleEnabled() || anyBusy}
          title={appleEnabled() ? undefined : t.auth.appleOff}
          onClick={() => run('apple', async () => loginWithApple(await appleIdentityToken()))}
        >
          {/* Логотип Apple рисуем контуром: символ  есть только на устройствах Apple */}
          <svg className="social-glyph" viewBox="0 0 24 24" width="17" height="17" aria-hidden="true">
            <path
              fill="currentColor"
              d="M16.3 12.8c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.2-2.8.9-3.5.9-.7 0-1.8-.8-3-.8-1.5 0-2.9.9-3.7 2.3-1.6 2.7-.4 6.8 1.1 9 .8 1.1 1.7 2.3 2.9 2.3 1.2 0 1.6-.7 3-.7s1.8.7 3 .7 2-1.1 2.8-2.2c.9-1.2 1.2-2.5 1.2-2.5s-2.4-.9-2.4-3.7zM14.1 5.9c.6-.8 1-1.9.9-3-.9 0-2 .6-2.7 1.4-.6.7-1.1 1.8-.9 2.9 1 .1 2-.5 2.7-1.3z"
            />
          </svg>
          {busy === 'apple' ? t.auth.appleBusy : t.auth.apple}
        </button>
      </div>

      <div className="or-rule">
        <span>{t.auth.or}</span>
      </div>

      <form onSubmit={submitEmail}>
        {guestMode ? (
          <label>
            {t.auth.deviceId}
            <input
              type="text"
              required
              value={customId}
              onChange={e => setCustomId(e.target.value)}
              placeholder={t.auth.deviceIdPlaceholder}
            />
          </label>
        ) : (
          <>
            {!login && (
              <label>
                {t.auth.nickname}
                <input
                  type="text"
                  required
                  minLength={3}
                  maxLength={25}
                  value={nickname}
                  onChange={e => setNickname(e.target.value)}
                  placeholder={t.auth.nicknamePlaceholder}
                />
              </label>
            )}
            <label>
              {t.auth.email}
              <input
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={e => setEmail(e.target.value)}
              />
            </label>
            <label>
              {t.auth.password}
              <input
                type="password"
                autoComplete={login ? 'current-password' : 'new-password'}
                required
                minLength={6}
                value={password}
                onChange={e => setPassword(e.target.value)}
              />
            </label>
          </>
        )}

        {error && <p className="form-error">{error}</p>}

        <button type="submit" className="btn btn-wide" disabled={anyBusy}>
          {busy === 'email'
            ? t.auth.busy
            : guestMode
              ? t.auth.submitGuest
              : login
                ? t.auth.submitLogin
                : t.auth.submitRegister}
        </button>
      </form>

      {login && (
        <button
          type="button"
          className="link-btn"
          onClick={() => {
            setGuestMode(v => !v);
            setError(null);
          }}
        >
          {guestMode ? t.auth.toEmail : t.auth.toGuest}
        </button>
      )}
    </div>
  );
}
