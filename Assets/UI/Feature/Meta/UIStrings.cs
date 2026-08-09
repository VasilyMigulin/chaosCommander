using Game.Core.Shared;

namespace AwesomeUI.Feature
{
    /// <summary>
    /// Локализованные строки мета-UI, задаваемые из КОДА (не через LocalizedText-компонент).
    /// Тянутся из таблицы CardText по ключу ui.* с RU-фолбэком (см. card_text.csv). Живая смена
    /// языка для этих строк — панель перечитывает при следующем открытии/UpdateView.
    /// </summary>
    public static class UIStrings
    {
        static string T(string key, string ru) => CardTextLocalization.GetText(key, ru);

        public static string Buy         => T("ui.meta.buy", "Купить");
        public static string Sell        => T("ui.meta.sell", "Продать");
        public static string Remove      => T("ui.meta.remove", "Снять");
        public static string Collect     => T("ui.meta.collect", "Забрать");
        public static string Streak      => T("ui.meta.streak", "Серия");
        public static string Reward      => T("ui.meta.reward", "Награда");
        public static string LoginReward => T("ui.meta.loginReward", "Награда за вход");
        public static string Purchase    => T("ui.meta.purchase", "Покупка");
        public static string NotEnough   => T("ui.meta.notEnough", "Недостаточно средств");
        public static string EnterPrice  => T("ui.meta.enterPrice", "Укажите цену");
        public static string Empty       => T("ui.meta.empty", "Пока пусто");
        public static string Confirm     => T("ui.dialog.confirm", "Подтвердить");
        public static string Cancel      => T("ui.dialog.cancel", "Отмена");

        // Реконнект (возврат в незавершённый бой после перезапуска приложения) — тосты в меню.
        public static string ReconnectResume  => T("ui.reconnect.resume",  "Кажется, вы пытались сбежать из прошлого боя. Возвращаем вас обратно…");
        public static string ReconnectWaiting => T("ui.reconnect.waiting", "Ваше место в бою ещё занято вами же. Ждём, пока сервер это заметит…");
        public static string ReconnectFailed  => T("ui.reconnect.failed",  "Побег удался: тот бой закончился без вас.");
        public static string Max         => T("ui.dialog.max", "Максимум");   // кнопка в блоке количества
        public static string BoosterTap    => T("ui.booster.tap", "Нажмите, чтобы открыть");
        /// <summary>Заголовок окна мульти-открытия: «Открыть ×3».</summary>
        public static string BoosterOpenN(int n) => string.Format(T("ui.booster.openN", "Открыть ×{0}"), n);
        public static string GameTitle     => T("ui.title.name", "Герои, Злодеи и Ублюдки");
        public static string TapToContinue => T("ui.title.tap", "Нажмите, чтобы продолжить");
        public static string RelatedCards  => T("ui.inspect.related", "Связанные карты");

        // ── Колоды (DeckViewPanel / DeckSlotView) ──
        // Подписи кнопок ставятся компонентом LocalizedText прямо в префабе (ui.deck.copy_code,
        // ui.deck.delete, ui.deck.add, ui.deck.save…). Здесь — только строки, которые задаёт КОД.
        public static string DeckDefaultName   => T("ui.deck.default_name", "Колода");
        public static string DeckCodeCopied    => T("ui.deck.code_copied", "Код колоды скопирован");
        /// <summary>{0} — имя колоды.</summary>
        public static string DeckDeleteConfirm => T("ui.deck.delete_confirm", "Удалить колоду «{0}»?");
        /// <summary>{0} — лимит колод.</summary>
        public static string DeckLimitReached  => T("ui.deck.limit", "Больше {0} колод держать нельзя — удалите одну");
        /// <summary>{0} — имя импортированной колоды.</summary>
        public static string DeckCodeImported  => T("ui.deck.code_imported", "Код из буфера обмена: «{0}»");
        /// <summary>{0} — имя колоды, которой пойдём в бой.</summary>
        public static string DeckSelected      => T("ui.deck.selected", "Играем колодой «{0}»");
        public static string DeckIncomplete    => T("ui.deck.incomplete", "Колода не собрана до конца — в бой не годится");
        public static string DeckMissingCards  => T("ui.deck.missing_owned", "В колоде есть карты, которых у вас нет");

        // ── Валюта: под капотом GD/GM (Gold/Gems), в игре — Бабосик (банкноты) / Безделушки (кристалл) ──
        // Имя валюты по коду для текстов наград/тултипов («+100 Бабосик»). Иконки — в InterfaceConfig.
        public static string CurrencyName(string code) => T("ui.currency." + code, code);
        public static string Dust => T("ui.card.dust", "Порвать");   // распыление карты в Обрывки
        /// <summary>«Порвать все дубли (×3)» — распыление всех лишних копий этой карты.</summary>
        public static string DustAll(int n) => string.Format(T("ui.card.dustAll", "Порвать дубли (×{0})"), n);
        public static string Sold => T("ui.market.sold", "Продано");  // оверлей недоступного оффера чёрного рынка

        /// <summary>
        /// Код отказа от сервера → человеческий текст. Сервер отвечает кодами (not_enough_currency,
        /// already_owned…), а не фразами: тексты и языки — дело клиента. Неизвестный код показываем как есть,
        /// чтобы в деве было видно, что именно вернул бэкенд.
        /// </summary>
        public static string BackendReason(string reason)
        {
            switch (reason)
            {
                case "not_enough_currency": return T("ui.err.not_enough", "Недостаточно средств");
                case "already_owned":       return T("ui.err.already_owned", "Уже куплено");
                case "unknown_item":        return T("ui.err.unknown_item", "Товар недоступен");
                case "not_owned":           return T("ui.err.not_owned", "Предмета нет в инвентаре");
                case "config_missing":      return T("ui.err.config_missing", "Раздел временно недоступен");
                case "grant_failed":        return T("ui.err.grant_failed", "Не удалось выдать предмет — средства возвращены");
                case "not_logged_in":       return T("ui.err.not_logged_in", "Нет связи с сервером");
                case "bad_request":         return T("ui.err.bad_request", "Некорректный запрос");
                case "dev_disabled":        return T("ui.err.dev_disabled", "Дев-функции выключены");
                // Аукцион
                case "auction_full":        return T("ui.err.auction_full", "Рынок переполнен — дождитесь закрытия лотов");
                case "has_bids":            return T("ui.err.has_bids", "На лот уже есть ставки — снять нельзя");
                case "outbid":              return T("ui.err.outbid", "Ставка должна быть выше текущей");
                case "own_lot":             return T("ui.err.own_lot", "Это ваш лот");
                case "ended":               return T("ui.err.ended", "Лот уже завершён");
                case "not_sellable":        return T("ui.err.not_sellable", "Этот предмет нельзя продавать");
                case "bad_currency":        return T("ui.err.bad_currency", "Недопустимая валюта");
                case "bid_too_low":         return T("ui.err.bid_too_low", "Ставка ниже минимальной");
                case "not_found":           return T("ui.err.not_found", "Лот не найден");
                case "not_owner":           return T("ui.err.not_owner", "Это не ваш лот");
                default:                    return reason;
            }
        }

        // ── Навигация / главное меню (обновлённая структура: единая кнопка «Играть») ──
        public static string Play        => T("ui.nav.play", "Играть");
        public static string NavBoosters => T("ui.nav.boosters", "Бустеры");
        public static string NavShop     => T("ui.nav.shop", "Магазин");
        public static string NavMarket   => T("ui.nav.market", "Рынок");
        public static string Journal     => T("ui.nav.tasks", "Журнал");   // раздел-хаб: задачи + вход + достижения
        public static string NavAuction  => T("ui.nav.auction", "Аукцион");

        // ── Журнал (подразделы) ──
        public static string JournalTasks        => T("ui.journal.tasks", "Задачи");
        public static string JournalLogin        => T("ui.journal.login", "Вход");
        public static string JournalDaily        => T("ui.journal.daily", "Ежедневные");
        public static string JournalWeekly       => T("ui.journal.weekly", "Еженедельные");
        public static string JournalAchievements => T("ui.journal.achievements", "Достижения");
        /// <summary>«День 3» — подпись дня в ленте входных наград.</summary>
        public static string LoginDay(int day) => string.Format(T("ui.journal.day", "День {0}"), day);
        public static string JournalReset        => T("ui.journal.reset", "Сброс через");
        public static string DayShort            => T("ui.journal.dayShort", "д");   // суффикс дней в таймере: «2д 14:22»
        public static string BlackMarket => T("ui.home.blackMarket", "Чёрный рынок");
        public static string Rank        => T("ui.home.rank", "Ранг");
        public static string Season      => T("ui.home.season", "Сезон");
        public static string LevelShort  => T("ui.home.level", "Ур.");

        // PvP / матчмейкинг здесь НЕ дублируем: поиск соперника уже полностью на FindOpponentPanel
        // (свой набор шутливых фраз в коде + ключи ui.matchmaking.found/loading/failed/cancelled/searching).
        // Кнопка запуска — ui.home.pvp («Рейтинговый бой»).

        // ── PvE / кампания ──
        public static string Campaign      => T("ui.pve.title", "Кампания");
        /// <summary>«глава 3 из 6» — подпись на карточке PvE и в шапке стори-мода.</summary>
        public static string ChapterOf(int chapter, int total)
            => string.Format(T("ui.pve.chapterOf", "глава {0} из {1}"), chapter, total);
        public static string CampaignDone  => T("ui.pve.campaignDone", "пройдена");
        public static string PveReward     => T("ui.pve.reward", "Награда");
        public static string PvePlay       => T("ui.pve.play", "Играть");
        public static string PveLocked     => T("ui.pve.locked", "Закрыто");
        public static string PveDone       => T("ui.pve.done", "Пройдено");
        /// <summary>«Бой N» для узлов кампании.</summary>
        public static string BattleN(int n) => $"{T("ui.pve.battle", "Бой")} {n}";

        // ── Профиль игрока ──
        public static string Profile             => T("ui.profile.title", "Профиль");
        public static string ProfileWins         => T("ui.profile.wins", "Победы");
        public static string ProfileLosses       => T("ui.profile.losses", "Поражения");
        public static string ProfileWinRate      => T("ui.profile.winrate", "Винрейт");
        public static string ProfileGames        => T("ui.profile.games", "Сыграно игр");
        public static string ProfileAchievements => T("ui.profile.achievements", "Достижения");
        public static string ProfileBoosters     => T("ui.profile.boosters", "Бустеров открыто");
        public static string ProfileCards        => T("ui.profile.cards", "Карт собрано");

        // ── Аукцион (ставки) ──
        public static string AuctionBid        => T("ui.auction.bid", "Ставка");
        public static string AuctionPlaceBid   => T("ui.auction.place", "Сделать ставку");
        public static string AuctionRaise      => T("ui.auction.raise", "Перебить");
        public static string AuctionMinBid     => T("ui.auction.minBid", "Старт");
        public static string AuctionSeller     => T("ui.auction.seller", "Продавец");
        public static string AuctionLeading    => T("ui.auction.leading", "Вы лидируете");
        public static string AuctionOutbid     => T("ui.auction.outbid", "Вас перебили");
        public static string AuctionNoBids     => T("ui.auction.noBids", "Ставок нет");
        public static string AuctionEnded      => T("ui.auction.ended", "Завершён");
        public static string AuctionList       => T("ui.auction.list", "Выставить");
        /// <summary>«Ставок: 3».</summary>
        public static string AuctionBids(int n) => string.Format(T("ui.auction.bids", "Ставок: {0}"), n);
        /// <summary>Хинт при почти полном рынке (фишка «лопнет»).</summary>
        public static string AuctionNearFull   => T("ui.auction.nearFull", "Ой, рынок вот-вот лопнет от лотов!");
        public static string AuctionFull       => T("ui.auction.full", "Рынок переполнен — дождитесь закрытия лотов");
        /// <summary>«Занято 38 / 40».</summary>
        public static string AuctionCapacity(int used, int max) => string.Format(T("ui.auction.capacity", "Занято {0} / {1}"), used, max);
        /// <summary>Итог к списанию с покупателя (со ставкой + комиссия): «Спишется 110 Бабосик».</summary>
        public static string AuctionBidCost(int total, string code) => string.Format(T("ui.auction.bidCost", "Спишется {0} {1}"), total, CurrencyName(code));
        /// <summary>Подтверждение ставки: «Ставка 100 → спишется 110 Бабосик?».</summary>
        public static string AuctionBidConfirm(int hammer, int total, string code)
            => string.Format(T("ui.auction.bidConfirm", "Ставка {0} → спишется {1} {2}?"), hammer, total, CurrencyName(code));
        /// <summary>Подтверждение выставления: «Выставить со старта 100 Бабосик на 60 мин?».</summary>
        public static string AuctionListConfirm(int minBid, string code, int minutes)
            => string.Format(T("ui.auction.listConfirm", "Выставить со старта {0} {1} на {2} мин?"), minBid, CurrencyName(code), minutes);

        /// <summary>Остаток времени лота: «59:31» или «1ч 05м» (>1ч). Для истёкшего — «Завершён».</summary>
        public static string AuctionTimeLeft(System.TimeSpan left)
        {
            if (left.TotalSeconds <= 0) return AuctionEnded;
            if (left.TotalHours >= 1) return string.Format(T("ui.auction.timeH", "{0}ч {1:00}м"), (int)left.TotalHours, left.Minutes);
            return string.Format("{0:00}:{1:00}", left.Minutes, left.Seconds);
        }

        // В текст-предложениях валюта идёт ИМЕНЕМ («Купить за 200 Бабосик?»), не кодом GD/GM.
        public static string BuyConfirm(int amount, string code)
            => string.Format(T("ui.dialog.buyConfirm", "Купить за {0} {1}?"), amount, CurrencyName(code));

        /// <summary>Покупка пачкой: «Купить 3 шт. за 900 Бабосик?» — количество в тексте, чтобы оно было
        /// видно даже если счётчик в префабе не привязан.</summary>
        public static string BuyConfirmMany(int count, int total, string code)
            => string.Format(T("ui.dialog.buyConfirmMany", "Купить {0} шт. за {1} {2}?"), count, total, CurrencyName(code));

        public static string SellConfirm(int amount, string code)
            => string.Format(T("ui.dialog.sellConfirm", "Выставить за {0} {1}?"), amount, CurrencyName(code));

        /// <summary>Человекочитаемое имя задачи по её типу.</summary>
        public static string TaskLabel(string type)
        {
            switch (type)
            {
                case "play_games":      return T("ui.task.play_games", "Сыграть игры");
                case "win_games":       return T("ui.task.win_games", "Победить в играх");
                case "play_cards":      return T("ui.task.play_cards", "Разыграть карты");
                case "open_boosters":   return T("ui.task.open_boosters", "Открыть бустеры");
                case "kill_creatures":  return T("ui.task.kill_creatures", "Уничтожить существ");
                case "summon_creatures":return T("ui.task.summon_creatures", "Призвать существ");
                case "spend_mana":      return T("ui.task.spend_mana", "Потратить ману");
                case "restore_mana":    return T("ui.task.restore_mana", "Восстановить ману");
                case "fill_board":      return T("ui.task.fill_board", "Заполнить свою сторону");
                case "charm_turns":     return T("ui.task.charm_turns", "Держать чары под контролем");
                case "deal_damage":     return T("ui.task.deal_damage", "Нанести урон врагам");
                case "deathrattle":     return T("ui.task.deathrattle", "Сработать «При смерти»");
                default:                return type;
            }
        }
    }
}
