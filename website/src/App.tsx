import { useEffect } from 'react';
import { BrowserRouter, Link, NavLink, Route, Routes, useLocation } from 'react-router-dom';
import DownloadButton from './components/DownloadButton';
import { LangProvider, LangSwitch, useLang } from './i18n/lang';
import Landing from './pages/Landing';
import Collection from './pages/Collection';

function ScrollManager() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (hash) {
      document.querySelector(hash)?.scrollIntoView({ block: 'start' });
    } else {
      window.scrollTo(0, 0);
    }
  }, [pathname, hash]);
  return null;
}

function NotFound() {
  const { t } = useLang();
  return (
    <main className="wrap notfound">
      <p className="eyebrow">404</p>
      <h1 className="section-title">{t.notFound.title}</h1>
      <p>
        <Link className="btn" to="/">
          {t.notFound.back}
        </Link>
      </p>
    </main>
  );
}

function Shell() {
  const { t } = useLang();
  return (
    <>
      <header className="site-head">
        <div className="wrap site-head-in">
          <Link to="/" className="wordmark">
            {t.nav.wordmark}
            <span>{t.nav.wordmarkSub}</span>
          </Link>
          <nav className="site-nav">
            <a href="/#game" className="nav-anchor">
              {t.nav.game}
            </a>
            <a href="/#campaign" className="nav-anchor">
              {t.nav.campaign}
            </a>
            <NavLink to="/collection" className="nav-link">
              {t.nav.collection}
            </NavLink>
            <LangSwitch />
            <DownloadButton small note={false} />
          </nav>
        </div>
      </header>

      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/collection" element={<Collection />} />
        <Route path="*" element={<NotFound />} />
      </Routes>

      <footer className="site-foot">
        <div className="hazard-rule" aria-hidden="true" />
        <div className="wrap site-foot-in">
          <p>
            <b>{t.hero.title}</b>
            {t.footer.linePre}
          </p>
          <p className="foot-dim">{t.footer.dim}</p>
        </div>
      </footer>
    </>
  );
}

export default function App() {
  return (
    <LangProvider>
      <BrowserRouter>
        <ScrollManager />
        <Shell />
      </BrowserRouter>
    </LangProvider>
  );
}
