import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from 'react';
import { Dict, Lang, STRINGS } from './strings';

const STORE_KEY = 'shh.lang';

/**
 * По умолчанию английский — основной рынок игры. Язык браузера НЕ учитываем
 * специально: русскоязычный посетитель тоже сначала видит EN-версию и при желании
 * переключается сам; его выбор запоминается.
 */
function detect(): Lang {
  try {
    const saved = localStorage.getItem(STORE_KEY);
    if (saved === 'ru' || saved === 'en') return saved;
  } catch {
    /* приватный режим — остаёмся на английском */
  }
  return 'en';
}

interface LangValue {
  lang: Lang;
  setLang: (l: Lang) => void;
  t: Dict;
}

const LangContext = createContext<LangValue | null>(null);

export function LangProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(detect);
  const t = STRINGS[lang];

  useEffect(() => {
    document.documentElement.lang = lang;
    document.title = t.meta.title;
    const meta = document.querySelector('meta[name="description"]');
    if (meta) meta.setAttribute('content', t.meta.description);
  }, [lang, t]);

  const setLang = useCallback((next: Lang) => {
    setLangState(next);
    try {
      localStorage.setItem(STORE_KEY, next);
    } catch {
      /* не смогли запомнить — не беда, язык переключился */
    }
  }, []);

  return <LangContext.Provider value={{ lang, setLang, t }}>{children}</LangContext.Provider>;
}

export function useLang(): LangValue {
  const ctx = useContext(LangContext);
  if (!ctx) throw new Error('useLang вызван вне <LangProvider>');
  return ctx;
}

/** Переключатель RU / EN для шапки. */
export function LangSwitch() {
  const { lang, setLang, t } = useLang();
  const other: Lang = lang === 'ru' ? 'en' : 'ru';
  return (
    <button
      type="button"
      className="lang-switch"
      onClick={() => setLang(other)}
      title={t.meta.otherLang}
      aria-label={t.meta.otherLang}
    >
      <span className={lang === 'ru' ? 'lang-on' : undefined}>RU</span>
      <i aria-hidden="true">/</i>
      <span className={lang === 'en' ? 'lang-on' : undefined}>EN</span>
    </button>
  );
}
