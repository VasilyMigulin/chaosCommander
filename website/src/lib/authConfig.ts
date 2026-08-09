/**
 * Настройки, которые нельзя вывести из кода — их заполняют руками один раз.
 * Подробности по каждому пункту: website/DEPLOY-AUTH.md
 *
 * Всё здесь ПУБЛИЧНОЕ: client id видно в любом браузере, это нормально.
 * Секреты (client secret Google, ключ Apple) живут только в PlayFab Game Manager.
 */

/**
 * OAuth 2.0 Client ID типа «Web application» из Google Cloud Console.
 * Тот же client id должен быть прописан в PlayFab → Add-ons → Google.
 * Пусто → кнопка «Google» показывается выключенной.
 */
export const GOOGLE_CLIENT_ID = '';

/**
 * Services ID из Apple Developer (Identifiers → Services IDs), например "ru.goreheroi.web".
 * Пусто → кнопка «Apple» показывается выключенной.
 */
export const APPLE_SERVICES_ID = '';

/**
 * Redirect URI, зарегистрированный у Apple для этого Services ID.
 * При usePopup: true Apple всё равно требует, чтобы адрес совпадал с зарегистрированным.
 * Обычно — сам сайт: "https://ваш-домен.ru/collection".
 */
export const APPLE_REDIRECT_URI = '';

/**
 * Ссылка на APK. По умолчанию — файл рядом с сайтом: положить его в dist/download/
 * (или в public/download/ до сборки). Можно заменить на внешнюю ссылку —
 * GitHub Releases, S3, Яндекс.Диск с прямой ссылкой.
 * ВНИМАНИЕ: у Cloudflare Pages лимит 25 МБ на файл — APK туда не влезет,
 * для него нужен внешний хостинг.
 */
export const APK_URL = '/download/secondhandheroes.apk';

/** Показывается рядом с кнопкой скачивания. Пусто — не показывается. */
export const APK_VERSION = '';
export const APK_SIZE = '';
