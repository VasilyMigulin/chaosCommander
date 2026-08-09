# Сайт «Горе-герои» / Second-Hand Heroes

Промо-сайт игры + личный кабинет: вход через PlayFab (тот же аккаунт, что в игре),
просмотр своей коллекции карт, кошелька и статистики профиля.

Стек: **Vite + React + TypeScript**. Хостится как обычная статика — сервера не нужно,
все запросы к PlayFab идут прямо из браузера.

```
website/
├─ index.html              — каркас страницы (шрифты, мета)
├─ public/                 — favicon, _redirects (SPA-фоллбэк для Cloudflare/Netlify)
├─ vercel.json             — SPA-фоллбэк для Vercel
├─ tools/extract-cards.mjs — генератор src/data/cards.json из Unity-ассетов
└─ src/
   ├─ styles.css           — вся тема («сукно и латунь»)
   ├─ lib/playfab.ts       — клиент PlayFab (title 180E80, только публичные вызовы)
   ├─ lib/cards.ts         — типы карт, редкости, элементы
   ├─ components/CardFrame.tsx — типографская рамка карты (арты не нужны)
   └─ pages/               — Landing (визитка), Collection (логин + коллекция)
```

---

## 1. Локальный запуск

Нужен [Node.js LTS](https://nodejs.org) (в системе его сейчас нет). Ставится в один клик
инсталлером или `winget install OpenJS.NodeJS.LTS`.

```bash
cd website
npm install        # один раз
npm run dev        # дев-сервер с горячей перезагрузкой → http://localhost:5173
npm run build      # прод-сборка в dist/
npm run preview    # посмотреть прод-сборку локально
```

## 2. Обновление данных карт

Сайт показывает карты из `src/data/cards.json` — снимка ростера. После изменения карт
или локализации в Unity перегенерируй его и закоммить:

```bash
npm run extract-cards
```

Скрипт читает `Assets/Resources/Expansion/Standard/**/*.asset` (статы, редкость, элементы,
стоимость) и `Assets/Localization/card_text.csv` (имена и описания RU/EN).

## 3. Настройка входа и раздачи APK

Вход и регистрация **по почте** работают сразу, без настройки. Кнопки **Google** и **Apple**
и ссылка на **APK** включаются заполнением одного файла — `src/lib/authConfig.ts`.
Пошагово (Google Cloud, Apple Developer, PlayFab Add-ons, куда класть APK) — в
[DEPLOY-AUTH.md](DEPLOY-AUTH.md).

## 4. Как устроен вход через PlayFab

- Всё общение — публичный **Client API** тайтла `180E80` (`https://180e80.playfabapi.com`).
  Секретных ключей на сайте нет и быть не должно (Developer Secret Key — только на сервере!).
- **Почта + пароль** — `LoginWithEmailAddress`, регистрация — `RegisterPlayFabUser` (ник
  задаётся сразу, поэтому игра его не переспросит).
- **Google** — `LoginWithGoogleAccount` с serverAuthCode из Google Identity Services;
  тот же метод, что и на Android, поэтому аккаунт один и тот же.
- **Apple** — `LoginWithApple` с id_token из Sign in with Apple JS.
- **ID устройства** — `LoginWithCustomID` с `CreateAccount:false` (новые аккаунты сайт не плодит).
  Запасной вход для тех, кто играл гостем.
- Коллекция и кошелёк — `GetUserInventory` (Economy v1): карты приходят как item id вида
  `standard_<cardId>`, валюты — GD/GM/SC.
- Статистика — CloudScript `GetProfile` (тот же контракт, что `PlayerProfileData` в игре).
  Если функция недоступна, блок просто не показывается.
- Сессия хранится в localStorage ~20 часов, потом попросит войти заново.

## 4. Куда и как залить (деплой)

Сайт — статика, подходит любой из вариантов ниже. Все дают бесплатный HTTPS и свой поддомен;
свой домен подключается в пару кликов.

### Вариант А — Cloudflare Pages (рекомендую)

Быстрый CDN, щедрый бесплатный тариф, автодеплой на каждый push.

1. Залей репозиторий на GitHub (сайт лежит внутри репо игры — это нормально).
2. [dash.cloudflare.com](https://dash.cloudflare.com) → **Workers & Pages → Create → Pages →
   Connect to Git** → выбери репозиторий.
3. Настройки сборки:
   - **Root directory:** `website`
   - **Build command:** `npm run build`
   - **Build output directory:** `dist`
4. Deploy. Получишь `https://<имя>.pages.dev`. SPA-роутинг уже настроен файлом
   `public/_redirects`.

Без GitHub тоже можно — прямая заливка папки:

```bash
npm run build
npx wrangler pages deploy dist --project-name gore-geroi
```

### Вариант Б — Vercel

1. [vercel.com](https://vercel.com) → **Add New → Project** → импортируй репозиторий.
2. **Root Directory:** `website` (фреймворк Vite определится сам).
3. Deploy → `https://<имя>.vercel.app`. Роутинг настроен через `vercel.json`.

### Вариант В — Netlify (самый простой, без git)

```bash
npm run build
```

Затем на [app.netlify.com/drop](https://app.netlify.com/drop) просто перетащи папку `dist`
в окно браузера. Всё. `_redirects` уже попадает в сборку.

### Вариант Г — свой сервер (VPS, хостинг, панель управления)

**Что заливать.** Только содержимое папки `dist/` — это и есть весь сайт. Node на сервере
НЕ нужен: `dist` — статика, её отдаёт обычный веб-сервер.

```bash
npm run build        # собирает dist/ заново
```

После сборки внутри `dist/` лежит:

```
dist/
├─ index.html                  ← точка входа (обязательно)
├─ favicon.svg                 ← иконка вкладки
├─ _redirects                  ← нужен только Netlify/Cloudflare; на своём сервере не мешает
└─ assets/
   ├─ index-<хеш>.js           ← весь код сайта (~312 КБ, вместе с данными карт)
   └─ index-<хеш>.css          ← все стили (~17 КБ)
```

Заливаем **содержимое** `dist/` (не саму папку) в корень сайта на сервере — обычно это
`/var/www/site/` или `public_html/`. По FTP/SFTP это просто перетаскивание четырёх объектов:
`index.html`, `favicon.svg`, `_redirects` и папки `assets/`.

⚠️ Имена файлов в `assets/` содержат хеш и **меняются при каждой сборке**. При обновлении
сайта старое содержимое `assets/` лучше удалить, а не докладывать поверх — иначе там
накопятся мёртвые файлы.

**Единственное требование к серверу** — отдавать `index.html` на любой путь (SPA-фоллбэк),
иначе прямая ссылка на `/collection` вернёт 404.

nginx:

```nginx
server {
    listen 80;
    server_name ваш-домен.ru;
    root /var/www/site;      # сюда положили содержимое dist/
    index index.html;

    location / {
        try_files $uri /index.html;
    }

    # необязательно: долгий кеш для файлов с хешем в имени
    location /assets/ {
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
}
```

Apache — положить рядом с `index.html` файл `.htaccess`:

```apache
RewriteEngine On
RewriteCond %{REQUEST_FILENAME} !-f
RewriteCond %{REQUEST_FILENAME} !-d
RewriteRule . /index.html [L]
```

**HTTPS обязателен.** Без него браузер заблокирует отправку пароля на PlayFab на многих
конфигурациях, да и логину по HTTP никто не поверит. Бесплатный сертификат: `certbot --nginx`.

### Свой домен

На Cloudflare/Vercel/Netlify: раздел Custom Domains → добавить домен → прописать у регистратора
CNAME, который они покажут. Сертификат выпускается автоматически.

## 5. Частые вопросы

- **CORS?** Не нужен: PlayFab Client API разрешает запросы из браузера с любого домена.
- **Это безопасно?** TitleId — публичная константа (она есть в каждом билде игры). Пароли
  уходят напрямую в PlayFab, сайт их не хранит.
- **Почему карты без картинок?** Артов пока нет и в игре — рамки типографские, по дизайну.
- **Гость не может войти по почте.** Да: сначала нужно зарегистрировать почту в игре
  (экран логина в игре умеет), дальше та же пара почта+пароль работает на сайте.
