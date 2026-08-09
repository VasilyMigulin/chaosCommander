/**
 * Тексты сайта. EN — это НЕ перевод RU, а вторая версия тех же шуток:
 * русская сторона шутит про барахолку и «горе-», английская — про комиссионку
 * и «sold as seen». Названия персонажей берём из локализации карт игры.
 */

const ru = {
  meta: {
    title: 'Горе-герои — Second-Hand Heroes',
    description:
      'Горе-герои — коллекционная карточная стратегия: колода, командир и бои на доске. Спасите принцессу от Гнидальфа Немытого — недорого.',
    otherLang: 'English version',
  },

  nav: {
    wordmark: 'Горе-герои',
    wordmarkSub: 'Second-Hand Heroes',
    game: 'Игра',
    campaign: 'Кампания',
    collection: 'Коллекция',
  },

  download: {
    button: 'Скачать APK',
    soon: 'Скачать — скоро',
    platform: 'Android',
  },

  hero: {
    eyebrow: 'Коллекционная карточная стратегия',
    title: 'Горе-герои',
    sub: 'Second-Hand Heroes',
    sticker: 'б/у',
    tagPre: 'Колода, командир и доска, на которой позиция бьёт пафос. Спасите Распрекрасную принцессу из лап ',
    tagBold: 'Гнидальфа Немытого',
    tagPost: ' — недорого.',
    chips: ['Бои на доске', '150+ карт', 'PvP и кампания', 'RU / EN'],
    cta: 'Моя коллекция',
  },

  game: {
    eyebrow: 'Правила барахолки',
    title: 'Что за игра',
    leadPre: 'Это коллекционная карточная игра, в которой карты не «выкладываются на стол», а выходят на клетчатую доску и ходят по ней. Мана копится, золото тратится, командир командует — а выигрывает тот, кто ',
    leadBold: 'дошёл до вражеского ряда',
    leadPost: ', а не тот, кто громче хлопнул картой.',
    features: [
      {
        title: 'Доска вместо стола',
        text: 'Существа выходят на ваш первый ряд и дальше ходят по клеткам. Позиция решает не меньше, чем статы.',
      },
      {
        title: 'Командир под рукой',
        text: 'Колода из 20 карт плюс командир в отдельном слоте. Погиб — отдышится ход и вернётся в строй.',
      },
      {
        title: 'Три типа карт',
        text: 'Существа нанимаются за золото, заклинания — за ману, а чары висят аурами и триггерами, не больше пяти за раз.',
      },
      {
        title: 'Экономика барахолки',
        text: 'Бустеры, распыление лишних карт в Обрывки, чёрный рынок и аукцион. Всё честно. Почти.',
      },
    ],
  },

  campaign: {
    eyebrow: 'Кампания',
    title: 'Похищение Распрекрасной принцессы',
    leadPre: 'Принцессу похитил Гнидальф Немытый — облезлый волшебник, возомнивший себя властелином. Спасать её отправляется ',
    leadBold: 'Шальной принц',
    leadPost: ': не самый острый меч в стойке, но упрямый до дурного. Шесть боёв — шесть горе-злодеев, каждый со своей идеей фикс.',
    fights: [
      {
        loc: 'Побег из замка',
        boss: 'Стражник',
        line: 'Первый урок героизма: ходить, бить и не подставляться под сдачу.',
      },
      {
        loc: 'Канализация',
        boss: 'Крысиный король',
        line: 'Здесь ничто не умирает зря: хрипы, рой крыс и фирменное амбре.',
      },
      {
        loc: 'Качалка',
        boss: 'Сенсей качалки',
        line: 'Билли жмёт от груди больше, чем весит ваша колода.',
      },
      {
        loc: 'Болото невезения',
        boss: 'Старый колдун',
        line: 'Проклятия замешиваются прямо в вашу колоду. Приятного добора.',
      },
      {
        loc: 'Преисподняя',
        boss: 'Чёртов начальник',
        line: 'Рой чертей и производственные совещания. Ад как он есть.',
      },
      {
        loc: 'Логово Гнидальфа',
        boss: 'Гнидальф Немытый',
        line: 'Финальный босс не мылся с прошлого патча. Спасите принцессу.',
      },
    ],
  },

  villains: {
    eyebrow: 'Витрина уценки',
    title: 'Галерея горе-злодеев',
    lead: 'Настоящие карты из ростера — статы честные, прямиком из игры. Арты ещё в мастерской, так что пока — чистая типографика и воображение.',
  },

  modes: {
    eyebrow: 'Режимы',
    title: 'Во что тут играть',
    items: [
      {
        title: 'PvP-дуэли',
        text: 'Онлайн-матчи один на один: каждое действие воспроизводится у оппонента снапшот в снапшот.',
      },
      {
        title: 'Кампания',
        text: 'Шесть боёв с репликами боссов, скриптовыми пакостями и наградами за упрямство.',
      },
      {
        title: 'PvE-спарринг',
        text: 'Бой с ИИ без очереди и нервов — обкатать колоду перед выходом в люди.',
      },
      {
        title: 'Два языка',
        text: 'Полная локализация RU / EN. Шутки написаны дважды, а не переведены один раз.',
      },
    ],
  },

  addon: {
    eyebrow: 'Первый аддон',
    sticker: 'скоро',
    title: 'Война Баклажаноса',
    lead: 'Сатира на супергеройский конвейер: Баклажанос собирает Стразы Бесконечности, «Общий сбор» разыгрывает карты из колоды цепочкой, пока не упрётся в тупик, а артефакты-экипировка возвращаются в руку — сколько бы носителей ни полегло.',
  },

  footer: {
    linePre: ' · Second-Hand Heroes — коллекционная карточная стратегия. В разработке.',
    dim: 'Сделано на Unity · © 2026 · Арты карт в пути — художник ещё торгуется.',
  },

  notFound: {
    title: 'Такой страницы нет даже на чёрном рынке',
    back: 'Вернуться на главную',
  },

  auth: {
    eyebrowLogin: 'Вход в лавку',
    eyebrowRegister: 'Новый герой',
    titleLogin: 'Моя коллекция',
    titleRegister: 'Регистрация',
    tabLogin: 'Вход',
    tabRegister: 'Регистрация',
    hintLogin: 'Один аккаунт на сайт и на игру. Входите тем же способом, что и в игре.',
    hintRegister: 'Аккаунт сразу подойдёт и для игры — на телефоне войдёте этой же почтой.',
    google: 'Через Google',
    apple: 'Через Apple',
    googleBusy: 'Google…',
    appleBusy: 'Apple…',
    googleOff: 'Вход через Google ещё не настроен',
    appleOff: 'Вход через Apple ещё не настроен',
    or: 'или почтой',
    nickname: 'Ник',
    nicknamePlaceholder: 'как вас звать на поле боя',
    email: 'Почта',
    password: 'Пароль',
    deviceId: 'Гостевой ID устройства',
    deviceIdPlaceholder: 'в игре: Настройки → Скопировать ID',
    submitLogin: 'Войти',
    submitRegister: 'Создать аккаунт',
    submitGuest: 'Войти по ID',
    busy: 'Стучимся…',
    toGuest: 'Играли гостем? Войти по ID устройства',
    toEmail: '← Обычный вход по почте',
    genericError: 'Что-то пошло не так. Попробуйте ещё раз.',
  },

  collection: {
    eyebrow: 'Личная барахолка',
    playerFallback: 'Игрок',
    collectedPre: 'Собрано ',
    collectedMid: ' из ',
    collectedPost: ' карт',
    signOut: 'Выйти',
    currencies: { GD: 'Бабосики', GM: 'Безделушки', SC: 'Обрывки' },
    stats: {
      level: 'уровень',
      record: 'победы–поражения',
      winRate: 'винрейт',
      boosters: 'бустеров открыто',
    },
    searchPlaceholder: 'Поиск по имени…',
    searchLabel: 'Поиск карты',
    filterAll: 'Все',
    filterTypeLabel: 'Тип карты',
    filterRarityLabel: 'Редкость',
    anyRarity: 'Любая редкость',
    ownedOnly: 'только собранные',
    loading: 'Пересчитываем сундуки…',
    nothing: 'Ничего не нашлось. Даже на чёрном рынке.',
    loadError: 'Не удалось загрузить коллекцию.',
    emptyPre: 'Карт пока нет. Скачайте игру и войдите ',
    emptyBold: 'этим же аккаунтом',
    emptyPost: ' — стартовый набор выдадут на первом запуске.',
  },

  card: {
    atk: 'атака',
    hp: 'жизни',
    spd: 'скр',
    notOwned: 'не найдена',
  },

  cardType: { creature: 'Существо', spell: 'Заклинание', charm: 'Чары' },
  cost: { gold: 'Золото', mana: 'Мана', health: 'Здоровье' },
  rarity: {
    common: 'Обычная',
    rare: 'Редкая',
    epic: 'Эпическая',
    legendary: 'Легендарная',
    exotic: 'Экзотическая',
  },
  rarityPlural: {
    common: 'Обычные',
    rare: 'Редкие',
    epic: 'Эпические',
    legendary: 'Легендарные',
    exotic: 'Экзоты',
  },
  element: {
    red: 'Красный',
    blue: 'Синий',
    green: 'Зелёный',
    yellow: 'Жёлтый',
    white: 'Белый',
    black: 'Чёрный',
  },
};

export type Dict = typeof ru;

const en: Dict = {
  meta: {
    title: 'Second-Hand Heroes',
    description:
      'Second-Hand Heroes — a collectible card game with a board: a deck, a commander and creatures that actually walk. Rescue the princess from Gnidalf the Unwashed. Rates negotiable.',
    otherLang: 'Русская версия',
  },

  nav: {
    wordmark: 'Second-Hand',
    wordmarkSub: 'Heroes · Sold as seen',
    game: 'The game',
    campaign: 'Campaign',
    collection: 'Collection',
  },

  download: {
    button: 'Get the APK',
    soon: 'Download — soon',
    platform: 'Android',
  },

  hero: {
    eyebrow: 'A collectible card game, gently used',
    // Неразрывный дефис U+2011: обычный дефис браузер рвёт, и заголовок распадается на три строки.
    title: 'Second‑Hand Heroes',
    sub: 'Sold as seen · No refunds',
    sticker: 'as is',
    tagPre: 'A deck, a commander and a board where position beats posturing. Rescue the Perfectly Lovely Princess from ',
    tagBold: 'Gnidalf the Unwashed',
    tagPost: ' — rates negotiable.',
    chips: ['Fights on a board', '150+ cards', 'PvP & campaign', 'EN / RU'],
    cta: 'My collection',
  },

  game: {
    eyebrow: 'House rules of the flea market',
    title: 'What this is',
    leadPre: 'A collectible card game where cards are not slapped onto a table — they step onto a squared board and walk about on it. Mana piles up, gold burns down, the commander commands, and the win goes to whoever ',
    leadBold: 'reaches the enemy back row',
    leadPost: ', not to whoever slaps hardest.',
    features: [
      {
        title: 'A board, not a tabletop',
        text: 'Creatures turn up on your front row and walk from there. Where a thing stands matters as much as what it hits for.',
      },
      {
        title: 'A commander on call',
        text: 'Twenty cards plus a commander in his own slot. Gets flattened, sulks for a turn, comes back for more.',
      },
      {
        title: 'Three kinds of cards',
        text: 'Creatures cost gold, spells cost mana, and charms hang about as auras and triggers — five at a time, no more.',
      },
      {
        title: 'Flea-market economics',
        text: 'Boosters, grinding spare cards down into Scraps, a black market and an auction house. All above board. Mostly.',
      },
    ],
  },

  campaign: {
    eyebrow: 'Campaign',
    title: 'The Abduction of the Perfectly Lovely Princess',
    leadPre: 'The princess has been carried off by Gnidalf the Unwashed — a moulting wizard with delusions of overlordship. Off to fetch her goes the ',
    leadBold: 'Daft Prince',
    leadPost: ': not the sharpest sword on the rack, but stubborn well past the point of sense. Six fights, six second-rate villains, each with one fixed idea.',
    fights: [
      {
        loc: 'Castle breakout',
        boss: 'The Guard',
        line: 'Heroism, lesson one: move, hit, and do not stand where the hitting happens.',
      },
      {
        loc: 'The sewers',
        boss: 'Rat King',
        line: 'Nothing dies for nothing down here: death rattles, rat swarms and a smell with its own postcode.',
      },
      {
        loc: 'The gym',
        boss: 'Gym Sensei',
        line: 'Billy benches more than your entire deck weighs.',
      },
      {
        loc: 'Bog of bad luck',
        boss: 'Old Warlock',
        line: 'Curses get shuffled straight into your deck. Enjoy your draw step.',
      },
      {
        loc: 'The underworld',
        boss: 'Imp Overseer',
        line: 'Imp swarms and mandatory team meetings. Hell, accurately depicted.',
      },
      {
        loc: "Gnidalf's lair",
        boss: 'Gnidalf the Unwashed',
        line: 'The final boss has not washed since the last patch. Go and fetch the princess.',
      },
    ],
  },

  villains: {
    eyebrow: 'The clearance shelf',
    title: 'Gallery of second-rate villains',
    lead: 'Real cards off the roster — the stats are honest, straight out of the build. The art is still at the framer’s, so for now it is type and imagination.',
  },

  modes: {
    eyebrow: 'Modes',
    title: 'Ways to play',
    items: [
      {
        title: 'PvP duels',
        text: 'One-on-one online. Every action replays on the other side, snapshot for snapshot.',
      },
      {
        title: 'Campaign',
        text: 'Six fights with talking bosses, scripted dirty tricks and rewards for sheer stubbornness.',
      },
      {
        title: 'PvE sparring',
        text: 'Fight the AI with no queue and no nerves — break a deck in before you meet actual people.',
      },
      {
        title: 'Two languages',
        text: 'Full EN / RU localisation. The jokes were written twice, not translated once.',
      },
    ],
  },

  addon: {
    eyebrow: 'First expansion',
    sticker: 'soon',
    title: 'The Eggplanos War',
    lead: 'A swipe at the superhero conveyor belt: Eggplanos collects the Rhinestones of Infinity, «Assemble!» chains cards out of your deck until it hits one it cannot play, and artefact gear always finds its way back to your hand — however many owners it outlives.',
  },

  footer: {
    linePre: ' · Горе-герои — a collectible card game. Still in the workshop.',
    dim: 'Made in Unity · © 2026 · Card art pending — the artist is still haggling.',
  },

  notFound: {
    title: 'This page is not even on the black market',
    back: 'Back to the front page',
  },

  auth: {
    eyebrowLogin: 'Mind the step',
    eyebrowRegister: 'New hero',
    titleLogin: 'My collection',
    titleRegister: 'Sign up',
    tabLogin: 'Sign in',
    tabRegister: 'Sign up',
    hintLogin: 'One account for the site and the game. Use whatever you use in-game.',
    hintRegister: 'The account works in the game too — sign in on your phone with the same email.',
    google: 'With Google',
    apple: 'With Apple',
    googleBusy: 'Google…',
    appleBusy: 'Apple…',
    googleOff: 'Google sign-in is not set up yet',
    appleOff: 'Apple sign-in is not set up yet',
    or: 'or by email',
    nickname: 'Nickname',
    nicknamePlaceholder: 'what to call you on the battlefield',
    email: 'Email',
    password: 'Password',
    deviceId: 'Guest device ID',
    deviceIdPlaceholder: 'in game: Settings → Copy ID',
    submitLogin: 'Sign in',
    submitRegister: 'Create account',
    submitGuest: 'Sign in with ID',
    busy: 'Knocking…',
    toGuest: 'Played as a guest? Sign in with your device ID',
    toEmail: '← Back to email sign-in',
    genericError: 'Something went wrong. Try again.',
  },

  collection: {
    eyebrow: 'Your own junk pile',
    playerFallback: 'Player',
    collectedPre: '',
    collectedMid: ' of ',
    collectedPost: ' cards collected',
    signOut: 'Sign out',
    currencies: { GD: 'Dosh', GM: 'Baubles', SC: 'Scraps' },
    stats: {
      level: 'level',
      record: 'wins–losses',
      winRate: 'win rate',
      boosters: 'boosters opened',
    },
    searchPlaceholder: 'Search by name…',
    searchLabel: 'Search cards',
    filterAll: 'All',
    filterTypeLabel: 'Card type',
    filterRarityLabel: 'Rarity',
    anyRarity: 'Any rarity',
    ownedOnly: 'owned only',
    loading: 'Counting the chests…',
    nothing: 'Nothing found. Not even on the black market.',
    loadError: 'Could not load your collection.',
    emptyPre: 'No cards yet. Grab the game and sign in with ',
    emptyBold: 'this same account',
    emptyPost: ' — the starter set lands on first launch.',
  },

  card: {
    atk: 'atk',
    hp: 'hp',
    spd: 'spd',
    notOwned: 'not owned',
  },

  cardType: { creature: 'Creature', spell: 'Spell', charm: 'Charm' },
  cost: { gold: 'Gold', mana: 'Mana', health: 'Health' },
  rarity: {
    common: 'Common',
    rare: 'Rare',
    epic: 'Epic',
    legendary: 'Legendary',
    exotic: 'Exotic',
  },
  rarityPlural: {
    common: 'Common',
    rare: 'Rare',
    epic: 'Epic',
    legendary: 'Legendary',
    exotic: 'Exotic',
  },
  element: {
    red: 'Red',
    blue: 'Blue',
    green: 'Green',
    yellow: 'Yellow',
    white: 'White',
    black: 'Black',
  },
};

export const STRINGS = { ru, en };
export type Lang = keyof typeof STRINGS;
