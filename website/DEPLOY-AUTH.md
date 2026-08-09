# Вход, регистрация и раздача APK — что нужно настроить руками

Код уже написан. Здесь — то, что нельзя сделать из редактора: ключи в чужих консолях
и файл APK на сервере. Все значения вписываются в один файл — `src/lib/authConfig.ts`.

---

## 0. Что работает прямо сейчас, без настройки

**Вход и регистрация по почте** — работают сразу, ничего настраивать не нужно.
`RegisterPlayFabUser` и `LoginWithEmailAddress` — публичные методы PlayFab, и титул
`180E80` уже поднят.

Кнопки **Google** и **Apple** пока показываются выключенными: в `authConfig.ts` пустые
идентификаторы. Заполните — включатся сами.

---

## 1. Google

### 1.1. Google Cloud Console

1. [console.cloud.google.com](https://console.cloud.google.com) → создать проект (или взять существующий).
2. **APIs & Services → OAuth consent screen**: тип External, заполнить название приложения,
   почту поддержки, домен сайта. Пока приложение в статусе Testing, войти смогут только
   аккаунты из списка Test users — для публичного запуска нужно нажать Publish.
3. **APIs & Services → Credentials → Create credentials → OAuth client ID**:
   - Application type: **Web application** (именно Web, даже для игры на Android);
   - **Authorized JavaScript origins**: `https://ваш-домен.ru` (и `http://localhost:5173` для разработки);
   - **Authorized redirect URIs**: можно оставить пустым — popup-режим их не использует.
4. Скопировать **Client ID** и **Client secret**.

### 1.2. PlayFab

Game Manager → ваш титул → **Add-ons → Google** → вставить тот же Client ID и Client secret → Install.

### 1.3. Сайт

```ts
// src/lib/authConfig.ts
export const GOOGLE_CLIENT_ID = '1234567890-xxxxxxxx.apps.googleusercontent.com';
```

### 1.4. Важно про Android

Сайт получает от Google **serverAuthCode** и отдаёт его в `LoginWithGoogleAccount` —
ровно тот же метод, которым будет входить игра на Android. Поэтому, когда будете
прикручивать Google Sign-In в Unity, используйте **тот же самый Web Client ID**
(в Google Sign-In это поле `requestServerAuthCode(webClientId)`). Тогда сайт и телефон
попадут в один и тот же аккаунт PlayFab. Разные client id → разные аккаунты у одного
человека, и это уже не починить задним числом.

> Если PlayFab ответит на веб-код ошибкой обмена (`InvalidGoogleToken`), запасной путь —
> перевести оба клиента (сайт и Android) на `LoginWithOpenIdConnect`: в PlayFab
> Add-ons → OpenID Connect завести провайдера с issuer `https://accounts.google.com`,
> а на сайте вместо кода передавать `id_token`. Это меняет тип идентичности, поэтому
> решать надо **до** того, как появятся живые игроки.

---

## 2. Apple

Нужен платный Apple Developer Program (~$99/год).

1. **Identifiers → App IDs**: создать App ID игры, включить capability *Sign in with Apple*.
2. **Identifiers → Services IDs**: создать Services ID (например `ru.goreheroi.web`) —
   это «client id» для веба. Включить *Sign in with Apple* → Configure:
   - Primary App ID: App ID из шага 1 (**обязательно**, иначе веб и iOS дадут разные аккаунты);
   - Domains: `ваш-домен.ru`;
   - Return URLs: `https://ваш-домен.ru/collection`.
3. Скачать файл верификации домена и положить его по пути
   `/.well-known/apple-developer-domain-association.txt` на сайте.
4. **Keys**: создать ключ с Sign in with Apple, скачать `.p8` (даётся один раз).
5. PlayFab → **Add-ons → Apple** → заполнить Bundle ID, Key ID, Team ID и содержимое `.p8`.
6. Сайт:

```ts
export const APPLE_SERVICES_ID = 'ru.goreheroi.web';
export const APPLE_REDIRECT_URI = 'https://ваш-домен.ru/collection';
```

> **Подводный камень.** В PlayFab одно поле Bundle ID, а токен из веба приходит с
> `aud = Services ID`, тогда как токен с iPhone — с `aud = App Bundle ID`. Если однажды
> появится iOS-сборка, один из двух путей может начать отваливаться по audience.
> Пока игра только на Android, это не мешает; перед релизом на iOS проверьте оба входа.

---

## 3. Кнопка «Скачать APK»

### 3.1. Куда класть файл

По умолчанию кнопка ведёт на `/download/gore-geroi.apk` — то есть файл лежит рядом с сайтом.
Положить его можно двумя способами:

- **до сборки** — в `website/public/download/gore-geroi.apk`, тогда `npm run build` сам
  положит его в `dist/` (папка `public/` копируется целиком);
- **после сборки** — прямо на сервер в `<корень сайта>/download/`, сайт пересобирать не нужно.

Второй способ удобнее: APK меняется чаще, чем сайт, и не надо гонять сотню мегабайт
через сборку.

### 3.2. Ограничения хостингов

| Хостинг | Ограничение |
|---|---|
| Свой сервер (nginx/Apache) | нет ограничений — рекомендуется |
| **Cloudflare Pages** | **25 МБ на файл** — APK почти наверняка не влезет |
| Netlify | мягкий лимит, крупные файлы лучше вынести |
| Vercel | статика до 100 МБ |

Если хостинг не тянет — положите APK во внешнее место и укажите прямую ссылку:

```ts
export const APK_URL = 'https://github.com/USER/REPO/releases/download/v0.1.0/gore-geroi.apk';
```

Годятся GitHub Releases (до 2 ГБ, бесплатно), Cloudflare R2, S3. Главное — чтобы ссылка
отдавала **сам файл**, а не HTML-страницу-обёртку: Яндекс.Диск и Google Drive по обычной
ссылке отдают страницу, и скачивание не начнётся.

### 3.3. IIS (Windows-хостинг)

Ничего делать не нужно: `public/web.config` уезжает в `dist/` при сборке и уже содержит
MIME-тип для `.apk` и SPA-фоллбэк. **Без него IIS отдаёт на `.apk` ошибку 404** — он
отказывается раздавать файлы с незнакомым расширением.

Одна оговорка: раздел `<rewrite>` работает, только если на сервере установлен модуль
**URL Rewrite**. Если прямая ссылка на `/collection` возвращает 404 — модуля нет, попросите
хостера его поставить.

### 3.4. nginx: чтобы APK скачивался, а не открывался

```nginx
location /download/ {
    types { application/vnd.android.package-archive apk; }
    add_header Content-Disposition "attachment";
}
```

### 3.5. Подпись версии

```ts
export const APK_VERSION = 'v0.1.0';
export const APK_SIZE = '86 МБ';
```

Появится подписью под кнопкой. Пусто — просто не показывается.

---

## 4. Как сайт и игра оказываются одним аккаунтом

Порядок, ради которого всё затевалось:

1. Игрок регистрируется **на сайте** — почтой, Google или Apple. Ник спрашивается сразу,
   поэтому в игре его больше не переспросят.
2. Ставит APK, запускает игру. Игра делает тихий гостевой вход по ID устройства и
   показывает экран знакомства.
3. Игрок жмёт **«Уже есть аккаунт»** и вводит почту с паролем.
4. После успешного входа игра вызывает `LinkCustomID(ID устройства, ForceLink: true)` —
   **устройство привязывается к его аккаунту**.
5. Со следующего запуска тихий вход по ID устройства попадает сразу в этот аккаунт.
   Логиниться больше не нужно никогда.

Что важно понимать про шаг 4: `ForceLink: true` **отбирает** устройство у гостевого
аккаунта, созданного на шаге 2. Всё, что игрок успел нафармить гостем, остаётся на том
гостевом аккаунте и становится недоступно. Для сценария «зарегался на сайте → поставил
игру» это ровно то, что нужно (гостю там и терять нечего). Слияние прогресса двух
аккаунтов PlayFab не умеет, и городить его вручную — отдельная большая история.

**Смена аккаунта посреди сессии не поддерживается специально.** Вход по почте доступен
только на экране знакомства — до того, как загрузятся инвентарь, колоды и профиль.
Если пустить его из настроек, в памяти останутся данные прошлого игрока, и это
разъедется молча. Хотите такую кнопку — она должна перезагружать сцену целиком.

---

## 5. Что собрать в префабе игры

Код `LoginPanel` ждёт (все поля опциональны — без них вход по почте просто не появится):

```
LoginPanel
  ├─ WelcomeRoot
  │    └─ HaveAccountBtn (Button)      → _signInOpenButton    (ui.welcome.have_account)
  └─ SignInRoot (GameObject)           → _signInRoot
       ├─ EmailInput    (TMP_InputField) → _signInEmailInput
       ├─ PasswordInput (TMP_InputField) → _signInPasswordInput   ⚠ Content Type = Password
       ├─ SubmitButton  (Button)         → _signInSubmitButton    (ui.signin.submit)
       └─ BackButton    (Button)         → _signInBackButton      (ui.signin.back)
```

Ключи локализации уже в `card_text.csv` (`ui.signin.*`, `ui.welcome.have_account`) —
после правки CSV не забыть «Import full CardText CSV».
