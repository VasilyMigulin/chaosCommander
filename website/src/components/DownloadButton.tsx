import { APK_SIZE, APK_URL, APK_VERSION } from '../lib/authConfig';
import { useLang } from '../i18n/lang';

/**
 * Кнопка скачивания APK. Ссылка прямая: download запускает загрузку сразу,
 * без промежуточной страницы. Если APK_URL пуст — кнопка выключена («скоро»).
 */
export default function DownloadButton({
  small = false,
  note = true,
}: {
  small?: boolean;
  note?: boolean;
}) {
  const { t } = useLang();
  const cls = 'btn btn-download' + (small ? ' btn-small' : '');
  const meta = [APK_VERSION, APK_SIZE].filter(Boolean).join(' · ');

  if (!APK_URL) {
    return (
      <span className={cls + ' btn-disabled'} aria-disabled="true">
        {t.download.soon}
      </span>
    );
  }

  return (
    <span className="download-slot">
      <a className={cls} href={APK_URL} download>
        <span className="dl-glyph" aria-hidden="true">
          ↓
        </span>
        {t.download.button}
      </a>
      {note && (
        <span className="download-note">
          {t.download.platform}
          {meta ? ` · ${meta}` : ''}
        </span>
      )}
    </span>
  );
}
