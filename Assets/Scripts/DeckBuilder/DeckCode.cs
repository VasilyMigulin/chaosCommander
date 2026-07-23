using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Game.Core.DeckBuilder
{
    /// <summary>
    /// Код колоды «как в Hearthstone»: вся колода пакуется в base64-строку, которой делятся через
    /// буфер обмена. Чистый C#, без Unity — живёт в DeckBuilder, доступен и UI, и сервисам.
    ///
    /// Формат (varint = LEB128, 7 бит на байт — так же, как у HS):
    ///   byte  0           резерв, ВСЕГДА 0 — по нему отличаем код колоды от произвольного текста
    ///   varint            версия формата (Version)
    ///   varint + utf8     имя колоды (может быть пустым)
    ///   varint            число аддонов, дальше по каждому: varint длина + utf8 id
    ///   varint, varint    командир: индекс аддона + id карты
    ///   varint N1         сколько карт лежит по 1 копии, дальше N1 × (индекс аддона, id карты)
    ///   varint N2         то же для 2 копий
    ///   varint Nn         прочие количества: Nn × (индекс аддона, id карты, количество)
    ///
    /// Карты (id аддона + id карты) — те же, что в OwnedCardData, поэтому код НЕ зависит от порядка
    /// карт в конфиге и не ломается при добавлении новых. Списки сортируются → одинаковая колода
    /// всегда даёт одинаковый код (удобно сравнивать/дедупить).
    ///
    /// Код НЕ проверяет, есть ли карты у игрока — это дело DeckBuildPanel (соберёт что может и
    /// скажет, чего не хватает). Незнакомые карты (чужой аддон/старый код) отбрасываются при импорте.
    /// </summary>
    public static class DeckCode
    {
        public const int Version = 1;

        // Санити-лимиты: код приезжает из буфера обмена, т.е. это НЕДОВЕРЕННЫЙ ввод. Без них битая
        // строка могла бы попросить аллоцировать список на миллиард элементов.
        const int MaxExpansions = 64;
        const int MaxCardKinds  = 256;
        const int MaxNameLength = 64;

        // ── Encode ───────────────────────────────────────────────────────────────

        /// <summary>Колода → код. null, если колоды нет или в ней нет командира.</summary>
        public static string Encode(SavedDeckData deck)
        {
            if (deck?.Commander.ExpansionId == null) return null;

            var expansions = new List<string>();
            int ExpIndex(string id)
            {
                id ??= string.Empty;
                int i = expansions.IndexOf(id);
                if (i < 0) { expansions.Add(id); i = expansions.Count - 1; }
                return i;
            }

            ExpIndex(deck.Commander.ExpansionId);   // аддон командира — почти всегда основной, пусть будет нулевым

            var ones = new List<OwnedCardData>();
            var twos = new List<OwnedCardData>();
            var many = new List<OwnedCardData>();

            if (deck.Cards != null)
            {
                foreach (var card in deck.Cards)
                {
                    if (card.Count <= 0) continue;
                    ExpIndex(card.ExpansionId);
                    if (card.Count == 1) ones.Add(card);
                    else if (card.Count == 2) twos.Add(card);
                    else many.Add(card);
                }
            }

            ones.Sort(CompareCards);
            twos.Sort(CompareCards);
            many.Sort(CompareCards);

            using var ms = new MemoryStream();

            ms.WriteByte(0);                       // резерв
            WriteVarint(ms, Version);
            WriteString(ms, deck.Name);

            WriteVarint(ms, expansions.Count);
            foreach (var exp in expansions) WriteString(ms, exp);

            WriteVarint(ms, ExpIndex(deck.Commander.ExpansionId));
            WriteVarint(ms, deck.Commander.CardId);

            WriteBlock(ms, ones, expansions, withCount: false);
            WriteBlock(ms, twos, expansions, withCount: false);
            WriteBlock(ms, many, expansions, withCount: true);

            return Convert.ToBase64String(ms.ToArray());
        }

        static void WriteBlock(Stream s, List<OwnedCardData> cards, List<string> expansions, bool withCount)
        {
            WriteVarint(s, cards.Count);
            foreach (var card in cards)
            {
                WriteVarint(s, expansions.IndexOf(card.ExpansionId ?? string.Empty));
                WriteVarint(s, card.CardId);
                if (withCount) WriteVarint(s, card.Count);
            }
        }

        // ── Decode ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Код → колода. false на любой мусор (в т.ч. на обычный текст из буфера обмена) — без исключений,
        /// вызывать можно на чём угодно.
        /// </summary>
        public static bool TryDecode(string code, out SavedDeckData deck)
        {
            deck = null;
            if (string.IsNullOrWhiteSpace(code)) return false;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(Sanitize(code)); }
            catch (FormatException) { return false; }

            if (bytes.Length < 4 || bytes[0] != 0) return false;

            try
            {
                using var ms = new MemoryStream(bytes);
                ms.ReadByte();                                     // резерв (уже проверен)

                if (ReadVarint(ms) != Version) return false;       // чужая/будущая версия формата

                string name = ReadString(ms, MaxNameLength);

                int expCount = ReadVarint(ms);
                if (expCount <= 0 || expCount > MaxExpansions) return false;
                var expansions = new string[expCount];
                for (int i = 0; i < expCount; i++) expansions[i] = ReadString(ms, MaxNameLength);

                int cmdExp = ReadVarint(ms);
                int cmdId  = ReadVarint(ms);
                if (cmdExp < 0 || cmdExp >= expCount) return false;

                var result = new SavedDeckData
                {
                    Name      = name,
                    Commander = new OwnedCardData(expansions[cmdExp], cmdId, 1),
                };

                if (!ReadBlock(ms, expansions, result.Cards, fixedCount: 1)) return false;
                if (!ReadBlock(ms, expansions, result.Cards, fixedCount: 2)) return false;
                if (!ReadBlock(ms, expansions, result.Cards, fixedCount: 0)) return false;

                deck = result;
                return true;
            }
            catch (EndOfStreamException) { return false; }   // строка оборвана — тоже просто «не код»
        }

        static bool ReadBlock(Stream s, string[] expansions, List<OwnedCardData> into, int fixedCount)
        {
            int n = ReadVarint(s);
            if (n < 0 || n > MaxCardKinds) return false;

            for (int i = 0; i < n; i++)
            {
                int exp = ReadVarint(s);
                int id  = ReadVarint(s);
                int count = fixedCount > 0 ? fixedCount : ReadVarint(s);

                if (exp < 0 || exp >= expansions.Length) return false;
                if (count <= 0 || count > 99) return false;

                into.Add(new OwnedCardData(expansions[exp], id, count));
            }
            return true;
        }

        /// <summary>Быстрая проверка «похоже на код колоды» — для кнопки «добавить колоду» (буфер обмена).</summary>
        public static bool LooksLikeCode(string text) => TryDecode(text, out _);

        // ── Низкий уровень ───────────────────────────────────────────────────────

        // Из буфера обмена строка часто приезжает с переносами/пробелами (мессенджеры переносят).
        static string Sanitize(string code)
        {
            var sb = new StringBuilder(code.Length);
            foreach (char c in code)
                if (!char.IsWhiteSpace(c)) sb.Append(c);
            return sb.ToString();
        }

        static int CompareCards(OwnedCardData a, OwnedCardData b)
        {
            int byExp = string.CompareOrdinal(a.ExpansionId ?? "", b.ExpansionId ?? "");
            return byExp != 0 ? byExp : a.CardId.CompareTo(b.CardId);
        }

        static void WriteVarint(Stream s, int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                s.WriteByte((byte)(v | 0x80));
                v >>= 7;
            }
            s.WriteByte((byte)v);
        }

        static int ReadVarint(Stream s)
        {
            int result = 0, shift = 0;
            while (true)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException();

                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;

                shift += 7;
                if (shift > 28) throw new EndOfStreamException();   // мусор: varint длиннее int
            }
        }

        static void WriteString(Stream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            WriteVarint(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        static string ReadString(Stream s, int maxLength)
        {
            int len = ReadVarint(s);
            if (len < 0 || len > maxLength * 4) throw new EndOfStreamException();   // utf8: до 4 байт на символ
            if (len == 0) return string.Empty;

            var buffer = new byte[len];
            int read = s.Read(buffer, 0, len);
            if (read != len) throw new EndOfStreamException();
            return Encoding.UTF8.GetString(buffer);
        }
    }
}
