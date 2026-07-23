using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game.Core.Model.Card;
using Game.Core.Shared.Interface;
using UnityEngine;

namespace Game.Core.Instance.Card
{
    /// <summary>
    /// Собирает «связанные» карты для данной карты — те, что она создаёт/замешивает/призывает
    /// через свои способности (Старый колдун → Вонючее облако, чара-призыв → Боец на арене).
    /// Нужна для предпросмотра в колодостроителе (CardInspectPopup), чтобы игрок видел,
    /// что именно карта порождает.
    ///
    /// ЧТО ПОКАЗЫВАЕМ: любая ссылка на карту в графе способностей (прямые поля Source/Card И карты
    /// внутри пулов) попадает в список, только если карта-кандидат — ТОКЕН (IsToken, показываются
    /// всегда) ИЛИ явно помечена CardModel.ShowAsLinked (обычные карты, порождаемые адресно).
    /// Обычные карты пулов без флага не показываются — иначе Фокус-покус вываливал бы весь пул.
    /// Никаких эвристик по именам полей: решение целиком на флаге самой карты.
    ///
    /// Как работает: обходит граф RuntimeAbilities рефлексией и собирает все ссылки на
    /// CardInstanceData (ICreatable) и пулы (ICardPool). Из CardInstanceData берётся уже готовый
    /// CardModel (поле CardData) — реестр не нужен. Глубина 1: разворачиваются карты, создаваемые
    /// ИСХОДНОЙ картой; их собственные создания не раскрываются (чтобы список не разрастался).
    ///
    /// Только UI/предпросмотр (не hot-path синхронизации), поэтому рефлексия здесь допустима.
    /// </summary>
    public static class RelatedCardsResolver
    {
        const int MaxDepth = 8;

        public static List<CardModel> Resolve(CardModel source)
        {
            var result = new List<CardModel>();
            if (source == null) return result;

            // RuntimeAbilities имеет тип List<Ability> (сборка Game.Core.Ability), которую Instance.Card
            // НЕ референсит (asmdef не трогаем). Достаём значение рефлексией и обходим как
            // System.Collections.IEnumerable (элементы как object) — так ссылка на сборку Ability не нужна,
            // обход графа и так рефлексивный (см. Walk).
            var field = typeof(CardModel).GetField("RuntimeAbilities",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!(field?.GetValue(source) is IEnumerable abilities)) return result;

            var seen = new HashSet<(string, int)> { (source.ExpansionId, source.Id) }; // исключить саму карту
            var visited = new HashSet<object>(ReferenceComparer.Instance);

            foreach (var ability in abilities)
                Walk(ability, 0, visited, seen, result);

            return result;
        }

        // Единый фильтр показа: токен — всегда; обычная карта — только с явным флагом ShowAsLinked.
        static void Collect(ICreatable creatable, HashSet<(string, int)> seen, List<CardModel> result)
        {
            var model = (creatable as CardInstanceData)?.CardData;
            if (model == null) return;
            if (!model.IsToken && !model.ShowAsLinked) return;
            if (seen.Add((model.ExpansionId, model.Id)))
                result.Add(model);
        }

        static void Walk(object obj, int depth, HashSet<object> visited, HashSet<(string, int)> seen, List<CardModel> result)
        {
            if (obj == null || depth > MaxDepth) return;

            // Ссылка на карту / пул — собираем (фильтр в Collect) и НЕ углубляемся внутрь них (глубина 1).
            if (obj is ICreatable creatable) { Collect(creatable, seen, result); return; }
            if (obj is ICardPool pool)
            {
                if (pool.Cards != null)
                    foreach (var c in pool.Cards)
                        if (c != null) Collect(c, seen, result);
                return;
            }

            var type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || obj is string) return;

            // Коллекции (List/массивы) — обходим элементы.
            if (obj is IEnumerable enumerable && !(obj is Object))
            {
                foreach (var item in enumerable)
                    Walk(item, depth + 1, visited, seen, result);
                return;
            }

            // Прочие UnityEngine.Object (спрайты, ассеты не-ICreatable) — не разворачиваем.
            if (obj is Object) return;

            // Разворачиваем только наши типы (способности/эффекты/значения), чтобы не гулять по фреймворку.
            if (type.Namespace == null || !type.Namespace.StartsWith("Game.Core")) return;
            if (!visited.Add(obj)) return; // защита от циклов

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (var f in fields)
                {
                    var ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;

                    object val;
                    try { val = f.GetValue(obj); }
                    catch { continue; }

                    Walk(val, depth + 1, visited, seen, result);
                }
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            bool IEqualityComparer<object>.Equals(object x, object y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object o)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
