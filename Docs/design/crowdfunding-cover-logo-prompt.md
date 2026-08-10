# Промт для AI-дизайна — обложка и лого краудфандинг-страницы

> Как пользоваться: это ДВА отдельных промта (лого и обложка) + важная оговорка про текст.
> Вставляй промты в image-gen (Midjourney/Ideogram/Recraft/Firefly и т.п.) по отдельности —
> одна генерация не должна пытаться сделать сразу и leftover-арт, и читаемый леттеринг.
> В конце — чек-лист файлов, которые реально нужны на странице кампании.

---

## ⚠️ Сначала прочитай: про текст в лого

Диффузионные генераторы **ненадёжно рисуют текст**, а кириллицу — особенно плохо (у игры два
названия: RU «Горе-герои», EN "Second-Hand Heroes"). Поэтому логотип делаем в два прохода:

1. **AI рисует ЭМБЛЕМУ/мастер-марку** (символ, без текста или с текстом-заглушкой) — см. промт A1.
2. **Название набирается вручную** реальным шрифтом поверх (Figma/Illustrator/Photoshop) —
   это надёжнее и правится без пересборки арта. Шрифт-кандидаты с полной кириллицей и
   свободной лицензией под этот стиль (жирный uppercase-гротеск): **Unbounded**, **Montserrat
   ExtraBold/Black**, **Golos Text Bold** — все есть на Google Fonts / доступны бесплатно.

Если хочешь попробовать AI и с текстом сразу — используй Ideogram или Recraft (они рисуют
текст заметно лучше Midjourney) и промт A2 (с текстом), но ВСЕГДА планируй ручную доводку.

---

# БРЕНД-ОСНОВА (не менять, если не уверен — держись этого)

- Название: RU **«Горе-герои»**, EN **"Second-Hand Heroes"** (не перевод — своя шутка на
  каждом рынке: барахолка/комиссионка неудачливых героев). EN-рынок приоритетный.
- Жанр: сатирическая коллекционная карточная игра с боем на клетчатой доске.
- Тон: трэш-балаган — смешно, нагло, слегка облезло, но **дорого сделано** (не любительский
  клипарт, а品 качественный постер с юмором).
- Палитра: грунт `#0B090E`/`#141018`, панели `#1E1826`, линии `#3A3145`, чернила `#EDE6DA`,
  акценты — янтарь `#F2A33C` (главный), красный `#C24B33`, зелёный `#6FA65A`.
- Визуальный язык: лучи-санберст, halftone-растр, диагональные «опасные» полосатые ленты,
  всё слегка накренено (±1.5°), стикеры/штампы/бирки-ценники, потёртости и заплатки.
- Мотив: карточные масти ♠ ♦ ♥ + ★ для командира. **Никаких шахматных фигур** в брендинге
  (доска/клетки как игровой элемент — можно, ферзи-пешки как иконография — нельзя).
- Сюжетный каст (гл. герои, узнаваемые лица франшизы): **Шальной принц** (собранный из
  барахла рыцарь-раздолбай, герой игрока), **Распрекрасная принцесса** (его цель),
  **Гнидальф немытый** (главный антагонист — грязный опустившийся колдун-бомж). Плюс
  архетипы существ для массовки: Работяги (стройка/спецовки), Черти (мелкая нечисть),
  Флора (растения-мутанты).

---

# ПРОМТ A — ЛОГОТИП

## A1. Эмблема/иконка (рекомендуемый заход — без текста)

```
Bold graphic emblem logo mark for a satirical trading-card game about washed-up,
thrift-store superheroes. NO TEXT, symbol only. Concept: a five-pointed tin star badge
(commander motif) fused with a playing-card suit shape (spade), made of scavenged /
patched-together junk metal — rivets, duct tape patch, a garage-sale price tag hanging
off a string. Flat vector illustration, bold thick black outlines, screen-print poster
style, halftone dot shading, slight asymmetric tilt (-2 degrees), sticker/badge silhouette
with a thin off-white border ring like a pin badge. Color palette: dark charcoal-purple
background #0B090E, amber #F2A33C as dominant fill, red #C24B33 and green #6FA65A as small
accent details only. High contrast, reads clearly at small size (app icon scale), centered
composition, transparent or solid dark background, no gradients, no photorealism, no 3D
render, no chess piece imagery, no generic superhero shield/comic logo cliché.
--style raw --v 6 --ar 1:1
```

Вариации, которые стоит нагенерить отдельно (меняй только конкретную фразу «Concept: …»):
- **Заплатка на щите** — щит из донышка мусорного бака с заклёпками и стикером «SALE».
- **Корона на локте кресла** (иронично «трон из хлама») с молью/дырками.
- **Меч-щётка** (швабра/метла как меч) в форме звезды.

## A2. Полный леттеринг-лого (если пробуешь Ideogram/Recraft — они держат текст)

```
Poster-style bold display logotype, extra-thick uppercase custom lettering, slight worn
stamped-ink texture like a cheap price-tag stamp. Text reads "SECOND-HAND HEROES" stacked
in two lines, letters slightly uneven / hand-cut look as if patched from mismatched scrap
signage, small torn sticker underneath with tagline "sold as seen". Playing-card suit icons
(spade, heart) integrated into the letterforms of the O's, one letter replaced by a tin star.
Dark background #0B090E, amber #F2A33C main letter fill with a thin red #C24B33 drop-shadow
offset, halftone dot texture inside the letters, diagonal warning-stripe tape element behind
the wordmark, whole lockup tilted -1.5 degrees. Comic pulp poster aesthetic, screen-print
grain, high contrast, no gradients, no soft 3D bevels, no generic fantasy MMO font, no serif.
--style raw --v 6 --ar 16:9
```

Для русской версии — тот же промт, замени текстовую строку на:
`Text reads "ГОРЕ-ГЕРОИ" in bold custom Cyrillic uppercase lettering` (жди, что кириллица
поплывёт сильнее английской — переснимай несколько раз, финальную доводку всё равно делай
руками реальным шрифтом).

## Что запросить дополнительно у генератора / доработать вручную
- Отдельно: **монохромная версия** (белый на прозрачном) и **одноцветная амбер-версия** —
  для футболок/стикеров наград бэкерам.
- Логотип должен читаться **иконкой 64×64** (сплошной, без мелких деталей) — если эмблема
  рассыпается в мелком размере, упрощай.

---

# ПРОМТ B — ОБЛОЖКА / КЛЮЧЕВОЙ АРТ (hero-баннер кампании)

Это главное изображение страницы — должно за 1 секунду продать жанр (ККИ + тактика на
доске) и тон (комедийный треш-фэнтези про барахолку).

## B1. Основной промт (широкий hero-баннер)

```
Wide cinematic key art poster for a satirical trading-card / tactics game called
"Second-Hand Heroes" — a flea-market parody of superhero fantasy. Chaotic hero group shot:
a scruffy knight-prince made of mismatched dented armor and a trash-can-lid shield
("Daft Prince") leaping forward mid-battle-cry, flanked by a grubby hunched wizard villain
in patched robes with a crooked staff made of a broom and duct tape ("filthy old sorcerer"),
a construction-worker ogre in a hi-vis vest swinging a shovel, a small mischievous imp/devil
creature, and a lumpy plant-monster made of overgrown weeds — all caught mid-chaotic clash
above a glowing checkerboard battlefield made of oversized playing cards laid out like
chess tiles, card backs showing card-suit motifs. Dynamic diagonal composition, dramatic
sunburst rays exploding from behind the central hero, halftone dot texture in the sky,
comic pulp adventure poster energy (think vintage circus poster meets trading-card box art),
scattered flea-market junk floating in the frame — bent shopping cart wheel, price tags,
a "SALE" sticker burst. Bold rim lighting in amber #F2A33C separating characters from a
dark charcoal-purple background #0B090E, secondary red #C24B33 and green #6FA65A accents
on effects/magic, painterly digital illustration with graphic poster flatting, thick
confident linework, high contrast, no photorealism, no soft pastel colors, no generic
D&D fantasy realism, no text baked into the image, leave clear empty space in the upper
third and lower third for logo and campaign UI overlay.
--style raw --v 6 --ar 16:9
```

## B2. Компактный вариант (для квадратного соц-превью)

Тот же промт, но замени `--ar 16:9` на `--ar 1:1` и добавь в конец:
`tighter composition, single hero (Daft Prince) centered, villain silhouette small in
background, leave empty margin on all sides for cropping safety.`

## B3. Вертикальный постер (опционально — награда бэкерам / полиграфия)

Тот же основной промт, `--ar 2:3`, добавь: `full-body vertical composition, hero group
stacked from foreground to background instead of side-by-side, board motif at the bottom
third.`

## Чего избегать (negative-промт / явно указать в тексте)
- Никаких настоящих логотипов/силуэтов Marvel/DC — только собственные пародийные персонажи.
- Никаких шахматных фигур (ферзь/конь/пешка) — доска да, фигуры нет.
- Не фотореализм, не «глянцевый мидкор-фэнтези» (Blizzard-стиль слишком чистый — нужнее
  грязнее, плакатнее, комиксовее).
- Не светлый/пастельный фон — грунт всегда тёмный.
- Не встраивать текст/лого в сам арт — их накладывают отдельно поверх готового изображения.

---

# ЧЕК-ЛИСТ ФАЙЛОВ ДЛЯ СТРАНИЦЫ КАМПАНИИ

1. **Hero-баннер** 16:9 (напр. 1920×1080) — обложка вверху страницы.
2. **Квадратное превью** 1:1 (1200×1200) — соц-шеринг/превью в ленте.
3. **Логотип, горизонтальная раскладка** (эмблема + леттеринг рядом) на прозрачном фоне.
4. **Логотип-иконка** (только эмблема, без текста) — квадрат, для аватарки кампании/паблика.
5. Опционально: 2–3 **портрета персонажей** крупным планом (Шальной принц, Гнидальф) тем же
   промтом B1 с заменой «wide group shot» на «single character portrait, waist-up» — под
   апдейты кампании и соцсети.
6. Опционально: **вертикальный постер** B3 — если платформа даёт награду-принт бэкерам.

Уточни на площадке (Kickstarter/Boomstarter/Planeta.ru/другая), какие точные размеры обложки
она требует — они иногда отличаются на десятки пикселей, генерацию проще сразу кропнуть под
финальный размер, чем перегенерировать.
