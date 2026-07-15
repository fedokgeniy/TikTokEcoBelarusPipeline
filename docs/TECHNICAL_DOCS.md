# TikTokEcoBelarusPipeline — Техническая документация

> Версия: актуальна для ветки `master` на момент июля 2026  
> Стек: .NET 10 · ASP.NET Core · Blazor Server · EF Core 9 · PostgreSQL 16 · Docker

---

## Содержание

1. [Обзор системы](#1-обзор-системы)
2. [Архитектура](#2-архитектура)
3. [TikTokApiClient — HTTP-клиент](#3-tiktokApiclient--http-клиент)
4. [Пагинация поиска: стратегия и эвристики](#4-пагинация-поиска-стратегия-и-эвристики)
5. [BelarusEcoScorer — система двойного скоринга](#5-belarusecoScorer--система-двойного-скоринга)
6. [CollectionPipeline — сбор видео](#6-collectionPipeline--сбор-видео)
7. [PipelineOrchestrator — оркестратор](#7-pipelineOrchestrator--оркестратор)
8. [CommentClassifierService — AI-классификация](#8-commentclassifierservice--ai-классификация)
9. [ChannelMonitorPipeline — мониторинг каналов](#9-channelmonitorpipeline--мониторинг-каналов)
10. [База данных](#10-база-данных)
11. [Blazor UI](#11-blazor-ui)
12. [Конфигурация](#12-конфигурация)
13. [Диагностические логи: полная схема](#13-диагностические-логи-полная-схема)
14. [Известные ограничения API](#14-известные-ограничения-api)

---

## 1. Обзор системы

TikTokEcoBelarusPipeline — это веб-приложение для автоматического сбора и анализа TikTok-контента, связанного с экологической тематикой в Беларуси. Система решает три основные задачи:

- **Сбор**: постраничный поиск видео по ключевым словам через RapidAPI TikTok endpoint
- **Фильтрация и скоринг**: двойная оценка каждого видео по двум осям — релевантность к Беларуси (BelarusScore) и к экологии (EcoScore)
- **Мониторинг**: отслеживание конкретных TikTok-каналов на появление новых видео и комментариев

Все компоненты запускаются и управляются через Blazor Server UI без сторонних инструментов.

---

## 2. Архитектура

### Слои системы

```
┌──────────────────────────────────────────────────────┐
│                   Blazor Server UI                    │
│   Dashboard · Pipeline · Videos · Channels · Queries  │
└────────────────────┬─────────────────────────────────┘
                     │ вызов RunAsync()
┌────────────────────▼─────────────────────────────────┐
│              PipelineOrchestrator (Singleton)          │
│   читает AppSettings · вызывает Pipeline · сохраняет  │
└──────────┬────────────────────────────────────────────┘
           │
    ┌──────▼──────────────────────┐
    │     CollectionPipeline      │
    │  итерирует SearchQueries    │
    │  фильтрует по DateFrom      │
    │  вызывает scorer            │
    └──────┬──────────────────────┘
           │                        
    ┌──────▼──────────┐    ┌────────────────────┐
    │ TikTokApiClient │    │  BelarusEcoScorer   │
    │  SearchVideos   │    │  ComputeScore()     │
    │  GetUserInfo    │    │  кэш в IMemoryCache │
    │  GetComments    │    └────────────────────┘
    └──────┬──────────┘
           │ HTTP
    ┌──────▼──────────────┐
    │ RapidAPI TikTok API │
    │ tiktok-api23...     │
    └─────────────────────┘

    ┌────────────────────────────────────┐
    │  ChannelMonitorPipeline (отдельно) │
    │  GetUserInfo → видео → комментарии │
    └────────────────────────────────────┘

    ┌───────────────────────────────────────────────┐
    │  CommentClassifierService                      │
    │  Anthropic Claude API → JSON ClassifyResult    │
    └───────────────────────────────────────────────┘

    ┌───────────────────────────────────────────────┐
    │  PostgreSQL 16  ←→  EF Core 9 (AppDbContext)   │
    └───────────────────────────────────────────────┘
```

### Дерево проекта

```
TikTokEcoBelarusPipeline/
├── Components/                  # Blazor UI
│   └── Pages/
│       ├── Dashboard.razor      — статистика, последние видео
│       ├── Pipeline.razor       — запуск, прогресс, результаты
│       ├── Videos.razor         — таблица видео с фильтрами
│       ├── Channels.razor       — управление отслеживаемыми каналами
│       ├── Comments.razor       — комментарии + AI-классификация
│       ├── Hashtags.razor       — хэштег-аналитика
│       ├── Queries.razor        — CRUD поисковых запросов
│       └── Scoring.razor        — настройка правил скоринга
├── Domain/
│   └── Entities/                # EF Core-сущности
├── Infrastructure/
│   ├── AppDbContext.cs
│   ├── SeedData.cs
│   ├── Configurations/
│   └── Repositories/
├── Pipeline/
│   ├── CollectionPipeline.cs
│   └── ChannelMonitorPipeline.cs
├── Services/
│   ├── TikTokApiClient.cs
│   ├── BelarusEcoScorer.cs
│   ├── PipelineOrchestrator.cs
│   ├── CsvExportService.cs
│   ├── CommentClassifierService.cs
│   └── VideoDeduplicationService.cs
├── Models/                      # DTO TikTok API
├── Migrations/
├── Program.cs
├── appsettings.json
└── docker-compose.yml
```

---

## 3. TikTokApiClient — HTTP-клиент

**Файл:** `Services/TikTokApiClient.cs`

Класс является единственной точкой взаимодействия с внешним TikTok API через RapidAPI. Все запросы идут на хост `tiktok-api23.p.rapidapi.com`.

### Инициализация

```csharp
public TikTokApiClient(HttpClient http, string apiKey)
```

При создании в `DefaultRequestHeaders` добавляются два обязательных заголовка RapidAPI:
- `x-rapidapi-host: tiktok-api23.p.rapidapi.com`
- `x-rapidapi-key: <ваш ключ>`

### Методы

#### `GetUserInfoAsync(uniqueId)` — GET /api/user/info

Получает информацию о пользователе по `@username`. Парсит из ответа:
- `userInfo.user` → `userId`, `secUid`, `nickname`, `avatarThumb`
- `userInfo.stats` → `videoCount`

Формирует `profileUrl` как `https://www.tiktok.com/@{uniqueId}`. Использует `SafeGetWithRetryAsync` с поддержкой ретраев на 202/204.

#### `GetUserInfoByIdAsync(userId)` — GET /api/user/info-by-id

Аналогично предыдущему, но поиск по числовому `userId`. Отличается структурой ответа — поля снейкейсом (`unique_id`, `avatar_thumb.url_list`). `VideoCount` возвращает `0`, т.к. endpoint не включает статистику.

#### `GetUserFollowingsAsync(secUid, maxCount)` — GET /api/user/followings

Постраничная загрузка подписок пользователя. Особенности:
- Пагинация через `maxCursor` в query string
- Поддерживает оба формата поля `uniqueId`/`unique_id`
- Смотрит несколько возможных полей курсора: `maxCursor`, `cursor`, `minCursor`, `minTime`
- Останавливается при пустой странице, отсутствии курсора или неизменившемся курсоре
- `hasMore` игнорируется (известный баг API)
- Задержка 900ms между страницами

#### `GetVideoCommentsAsync(videoId, pageSize, maxPages)` — GET /api/post/comments

> ⚠️ Старый endpoint `/api/comment/list` удалён провайдером (HTTP 404). Используется `/api/post/comments`.

Возвращает `IAsyncEnumerable<TikTokComment>`. Для каждого комментария извлекается:
- `cid` — уникальный ID комментария
- `text` — текст
- `digg_count` — лайки
- `reply_comment_total` — число ответов
- `create_time` → конвертируется в `DateTimeOffset`
- `user.uid` / `user.unique_id` — автор

Останавливается когда `hasMore=false` или курсор не изменился. Задержка 800ms между страницами.

### Механизм ретраев (`SafeGetWithRetryAsync`)

Применяется для эндпоинтов, которые могут вернуть `202 Still Processing` или `204 No Content`:

```
Попытка 1: HTTP 202 → ждём 5s  → Попытка 2
Попытка 2: HTTP 202 → ждём 10s → Попытка 3
Попытка 3: HTTP 202 → ждём 15s → Попытка 4
Попытка 4: HTTP 202 → отдаём null, останавливаем
```

HTTP 4xx и 5xx — немедленный возврат `null` без ретраев.

---

## 4. Пагинация поиска: стратегия и эвристики

**Метод:** `TikTokApiClient.SearchVideosAsync(keyword, maxPages, delayMs, ct)`

Это наиболее сложная часть системы. RapidAPI TikTok endpoint имеет серьёзные ограничения пагинации, которые делают стандартные подходы нерабочими.

### Известные проблемы эндпоинта

| Проблема | Поведение | Решение |
|---|---|---|
| `cursor` зависает | Возвращает `cursor=12` на 20+ страницах подряд | Игнорировать cursor полностью |
| `has_more=0` некорректен | API возвращает 0, но данные ещё есть | Игнорировать has_more |
| Пул видео мал (~30-50) | API возвращает один и тот же набор | Jitter offset при дублях |
| Перекрытие страниц | При малом offset возвращаются те же видео | MinOffsetStep = 30 |

### Константы пагинации

```csharp
const int MinOffsetStep      = 30;   // минимальный шаг даже при raw=10
const int DupJitterStep      = 20;   // дополнительный прыжок за каждый streak
const int EmptyPageLimit     = 2;    // стоп при 2 пустых подряд
const int DuplicatePageLimit = 3;    // стоп при 3 дублирующих подряд
const int LowYieldPageLimit  = 5;    // предупреждение при 5 страницах с new<2
```

### Алгоритм шага offset

На каждой странице:

```
baseStep = max(rawCount, MinOffsetStep)   // не меньше 30 даже если API вернул 10 видео

Если newCount > 0:
    offsetNext = offset + baseStep        // обычный шаг

Если newCount == 0 (дублирующая страница):
    jitter     = DupJitterStep * duplicatePageStreak
    offsetNext = offset + baseStep + jitter
    
    // streak=1 → jitter=+20 → шаг=50
    // streak=2 → jitter=+40 → шаг=70
    // streak=3 → jitter=+60 → остановка
```

### Стрик-счётчики и правила остановки

Система использует три независимых счётчика вместо одного условия:

```
emptyPageStreak     — подряд страниц с пустым itemList
duplicatePageStreak — подряд страниц с newCount=0
lowYieldStreak      — подряд страниц с newCount < 2 (только логирование)
```

**Правила остановки (в порядке приоритета):**
1. `emptyPageStreak >= 2` → `[SEARCH END] repeated empty pages`
2. `duplicatePageStreak >= 3` → `[SEARCH END] repeated duplicate pages`
3. `page >= maxPages` → `[SEARCH END] max pages reached`
4. HTTP ошибка → `[SEARCH END] HTTP error or null body`
5. Ошибка парсинга JSON → `[SEARCH END] parse error`
6. `CancellationToken` → `OperationCanceledException`

Ключевое свойство: `duplicatePageStreak` **сбрасывается** при первом же новом видео. Это позволяет продолжить поиск после временной серии дублей.

### Пример работы (из реальных логов)

```
[SEARCH] Page 9/100  offset=240
[SEARCH OK] Page 9:  raw=10 new=0 baseStep=30 jitter=+20 offsetNext=290 → duplicatePageStreak=1/3
[SEARCH] Page 10/100 offset=290
[SEARCH OK] Page 10: raw=10 new=0 baseStep=30 jitter=+40 offsetNext=360 → duplicatePageStreak=2/3
[SEARCH] Page 11/100 offset=360
[SEARCH OK] Page 11: raw=10 new=2 baseStep=30 offsetNext=390           → streak сброшен ✓
[SEARCH] Page 12/100 offset=390
[SEARCH OK] Page 12: raw=10 new=0 baseStep=30 jitter=+20 offsetNext=440 → duplicatePageStreak=1/3
```

---

## 5. BelarusEcoScorer — система двойного скоринга

**Файл:** `Services/BelarusEcoScorer.cs`

### Входные данные

Принимает `TikTokItem` — распарсенный объект видео со следующими полями для анализа:
- `Desc` — описание видео
- `Author.Signature` — биография автора
- `Author.Nickname` / `Author.UniqueId` — имя аккаунта
- `AllHashtags` — массив хэштегов
- `Author.Verified` — верификация аккаунта

### Два независимых счёта

```csharp
scored.BelarusScore = ComputeScore(item, "belarus", rules, thresholds, signals);
scored.EcoScore     = ComputeScore(item, "eco",     rules, thresholds, signals);
```

Оба счёта вычисляются независимо через одну функцию `ComputeScore` с разным `scoreType`.

### Контексты поиска ключевых слов

Каждое правило (`ScoringRule`) имеет поле `SearchContext`, которое определяет где искать:

| SearchContext | Что проверяется |
|---|---|
| `"hashtags"` | Только хэштеги видео |
| `"bio"` | Только биография автора |
| `"description"` | Только описание видео |
| `"any"` (дефолт) | Описание + биография + nickname + uniqueId + хэштеги |

### Два режима подсчёта очков

#### Линейный (без `ScoringRuleThreshold`)

```
score += matches * weight
```

#### Ступенчатый (с `ScoringRuleThreshold`)

```
Если matches >= threshold.MinMatchCount → score += threshold.ScoreBonus
Берётся наибольший применимый бонус (сортировка DESC по MinMatchCount)
```

Позволяет задавать нелинейные бонусы. Например: 1 совпадение → +0.10, 3 совпадения → +0.25.

### Группировка по категориям и `MaxMatches`

Правила группируются по `Category`. Для каждой категории учитывается не более `MaxMatches` совпадений. Это предотвращает накопление избыточного score от одной категории.

### Специальные бонусы BelarusScore

Вне системы правил жёстко задано два дополнительных условия:

```csharp
// Флаг Беларуси в биографии автора
if (scoreType == "belarus" && item.Author.Signature.Contains("🇧🇾"))
    score += 0.35;

// Верифицированный аккаунт с ненулевым score
if (scoreType == "belarus" && item.Author.Verified && score > 0.2)
    score += 0.10;
```

### Нормализация и кэширование

Итоговый счёт обрезается до `[0.0, 1.0]` через `Math.Min(score, 1.0)`.

Правила кэшируются в `IMemoryCache` с TTL 30 минут (`ScoringCache:TtlMinutes`). Без кэша каждое из тысяч видео делало бы отдельный запрос к БД.

### Дефолтные правила (SeedData)

| Категория | Ключевые слова | Weight | MaxMatches | Режим |
|---|---|---|---|---|
| `Belarus_Explicit` | беларусь, belarus, bielarus, 🇧🇾, by | 0.40 | 1 | линейный |
| `Belarus_City` | минск, minsk, гродно, брест, витебск, могилев, гомель | 0.25 | 1 | линейный |
| `Belarus_Place` | беловежская, нарочь, налибоки, припять, неман | 0.30 | 1 | линейный |
| `Belarus_Language` | прырода, экалогія, лес | 0.10 | 99 | ступенчатый |

---

## 6. CollectionPipeline — сбор видео

**Файл:** `Pipeline/CollectionPipeline.cs`

### Входные параметры RunAsync

```csharp
Task<List<(ScoredVideo, Guid QueryId)>> RunAsync(
    double minBelarus   = 0.15,
    double minEco       = 0.20,
    int    maxVideos    = 50,
    CancellationToken ct = default)
```

- `minBelarus` / `minEco` — пороги фильтрации (передаются из UI при запуске)
- `maxVideos` — целевое число прошедших фильтр видео **на один запрос**
- `maxPagesHardLimit = 100` — абсолютный потолок страниц (константа внутри)

### Поток выполнения

```
Шаг 1: Загрузить активные SearchQuery, отсортировать по Priority

Для каждого query:
  Шаг 2: Создать LinkedCancellationTokenSource (ct + внутренний cts)
  
  Шаг 3: await foreach (TikTokItem item in api.SearchVideosAsync(linkedToken))
  
    Фильтр 1: item.IsAd || item.Author.PrivateAccount → пропустить
    Фильтр 2: item.CreatedAt < query.DateFrom → пропустить с логом
    Шаг 4:    scoredVideo = await scorer.ScoreAsync(item)
    Фильтр 3: !PassesThreshold(minBelarus, minEco) → пропустить
    
    passed++, queryVideos.Add((scoredVideo, query.Id))
    
    Если passed >= maxVideos:
      cts.Cancel()   ← прерывает SearchVideosAsync
      break
  
  Шаг 5: results.AddRange(queryVideos)
  Шаг 6: UpdateLastRunAtAsync(query.Id)

Вернуть results.OrderByDescending(TotalScore)
```

### Механизм остановки через CancellationToken

При достижении `maxVideos` пайплайн вызывает `cts.Cancel()`. `SearchVideosAsync` бросает `OperationCanceledException`, которая перехватывается:

```csharp
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
    // Это наша внутренняя остановка — игнорируем
}
```

Внешняя отмена от пользователя проходит через основной `ct` и не перехватывается.

### Диагностика нулевых результатов

Если `passed=0`, выводятся топ-5 видео с лучшими score для диагностики:

```
[SCORES] Прошли порог 0/41. Лучшие:
    BY=0.40 ECO=0.39 [✗] @username: описание видео...
```

---

## 7. PipelineOrchestrator — оркестратор

**Файл:** `Services/PipelineOrchestrator.cs`

Singleton-сервис. Создаёт `IServiceScope` на каждый запуск для получения scoped-зависимостей.

### RunAsync — полный поток

1. **Читает `maxPerQuery`** из `AppSettings` (дефолт = 50)
2. **Сообщает прогресс** через `IProgress<PipelineProgress>` → Blazor обновляет progress bar
3. **Запускает** `CollectionPipeline.RunAsync(...)`
4. **Сохраняет видео** в БД:
   - Существующее → обновляет счётчики (лайки, просмотры, комментарии, репосты)
   - Новое → создаёт `Video` entity с полным набором метаданных и `ScoreBreakdown`
5. **Создаёт M:N связи** `VideoSearchQueryLink` с защитой от дублей через `HashSet<(videoId, queryId)>` в памяти (EF Change Tracker не видит несохранённые объекты при `AnyAsync`)
6. **Сохраняет CSV** через `CsvExportService`
7. **Возвращает** `PipelineRunResult { Found, Saved, Skipped, Duration }`

### ScoreBreakdown

```json
{
  "belarus": ["Belarus_Explicit:беларусь", "flag:🇧🇾 in bio"],
  "eco":     ["Eco_Nature:природа", "Eco_Action:экология"]
}
```

Позволяет в UI показать точные причины, почему видео прошло фильтр.

---

## 8. CommentClassifierService — AI-классификация

**Файл:** `Services/CommentClassifierService.cs`

Использует **Anthropic Claude** для классификации комментариев. Ключ, модель и промпт конфигурируются через таблицу `AppSettings`.

### Конфигурационные ключи

| Ключ | По умолчанию | Описание |
|---|---|---|
| `anthropic:apiKey` | — | API ключ Anthropic (обязателен) |
| `anthropic:model` | `claude-haiku-4-5` | Модель |
| `anthropic:systemPrompt` | встроенный | Системный промпт |

### Дефолтный системный промпт

```
Ты — классификатор комментариев для экологической горячей линии «Зелёный телефон» (Беларусь).
Для КАЖДОГО комментария верни JSON-объект:
{"cid":"<id>","score":<0-100>,"category":"<категория>","shouldReply":<true|false>,"tags":[...]}.
Верни ТОЛЬКО JSON-массив, без markdown, без преамбулы.
```

### Батчинг (`MaxBatchChars = 6000`)

```
Для каждого комментария: len = cid.Length + text.Length + 20
Если chars + len > 6000 → закрыть батч, начать новый
Задержка 500ms между батчами
```

### ClassifyResult

```csharp
public record ClassifyResult(
    bool    IsRelevant,   // score >= 70
    int     Score,        // 0–100
    string? Category,     // категория комментария
    bool    ShouldReply,  // требует ответа
    string? Tags);        // теги через запятую
```

### Парсинг ответа Claude

Claude может обернуть JSON в markdown. Сервис находит массив через:

```csharp
int start = rawText.IndexOf('[');
int end   = rawText.LastIndexOf(']');
var items = JsonDocument.Parse(rawText[start..(end + 1)]);
```

---

## 9. ChannelMonitorPipeline — мониторинг каналов

**Файл:** `Pipeline/ChannelMonitorPipeline.cs`

Мониторит отслеживаемые TikTok-каналы на появление новых видео.

### Алгоритм

```
Для каждого активного TrackedChannel:
  1. GetUserInfoAsync(channel.UniqueId) → videoCount
  
  2. Если videoCount > channel.LastVideoCount:
     → запрашиваем последние (delta) видео пользователя
  
  3. Для каждого нового видео:
     - Если videoId нет в TrackedChannelVideos → сохраняем
     - Загружаем комментарии через GetVideoCommentsAsync
     - Сохраняем VideoComment
  
  4. channel.LastVideoCount = videoCount
  5. SaveChangesAsync
```

---

## 10. База данных

### Схема (основные таблицы)

#### `Videos`

| Поле | Тип | Описание |
|---|---|---|
| `VideoId` | `varchar` PK | TikTok video ID |
| `VideoUrl` | `text` | Ссылка на видео |
| `AuthorUniqueId` | `varchar` | @username автора |
| `AuthorNickname` | `varchar` | Отображаемое имя |
| `AuthorBio` | `text` | Биография автора |
| `Description` | `text` | Описание видео |
| `Hashtags` | `text[]` | Массив хэштегов |
| `BelarusScore` | `decimal` | Score 0.00–1.00 |
| `EcoScore` | `decimal` | Score 0.00–1.00 |
| `ScoreBreakdown` | `jsonb` | Детализация сигналов |
| `MatchedKeywords` | `text[]` | Совпавшие ключевые слова |
| `LikeCount` | `bigint` | Лайки |
| `CommentCount` | `bigint` | Комментарии |
| `ShareCount` | `bigint` | Репосты |
| `ViewCount` | `bigint` | Просмотры |
| `PublishedAt` | `timestamp?` | Дата публикации |
| `FetchedAt` | `timestamp` | Дата сбора |
| `UpdatedAt` | `timestamp` | Дата обновления |

#### `SearchQueries`

| Поле | Тип | Описание |
|---|---|---|
| `Id` | `uuid` PK | |
| `Value` | `varchar` | Ключевое слово |
| `QueryType` | `varchar` | `keyword` / `hashtag` |
| `IsActive` | `bool` | Активен ли |
| `Priority` | `int` | Порядок обработки |
| `DateFrom` | `date?` | Фильтр «не старше» |
| `LastRunAt` | `timestamp?` | Последний запуск |

#### `ScoringRules`

| Поле | Тип | Описание |
|---|---|---|
| `Id` | `uuid` PK | |
| `ScoreType` | `varchar` | `belarus` / `eco` |
| `Category` | `varchar` | Группа правила |
| `Keyword` | `varchar` | Ключевое слово |
| `Weight` | `decimal` | Вес совпадения |
| `MaxMatches` | `int` | Макс. учитываемых |
| `SearchContext` | `varchar` | Где искать |
| `IsActive` | `bool` | Активно ли |

#### `AppSettings`

| Key | Описание |
|---|---|
| `maxPerQuery` | Целевое число видео на запрос |
| `anthropic:apiKey` | Ключ Claude API |
| `anthropic:model` | Модель Claude |
| `anthropic:systemPrompt` | Системный промпт классификатора |

### Миграции

Применяются автоматически при старте:

```csharp
await db.Database.MigrateAsync();
```

Seed-данные (`SeedData.cs`) добавляются один раз при первом запуске.

---

## 11. Blazor UI

| Страница | Функция |
|---|---|
| `Dashboard.razor` | Сводная статистика: число видео, каналов, последние добавленные |
| `Pipeline.razor` | Форма запуска с настройкой порогов, progress bar, итоги |
| `Videos.razor` | Таблица видео с сортировкой, фильтрами, ScoreBreakdown |
| `Channels.razor` | Управление TrackedChannels: добавить/удалить, запустить мониторинг |
| `Comments.razor` | Комментарии к видео + запуск AI-классификации |
| `Hashtags.razor` | Агрегированная аналитика хэштегов |
| `Queries.razor` | CRUD для SearchQuery: создать, изменить Priority, DateFrom |
| `Scoring.razor` | CRUD для ScoringRule: ключевые слова, веса, контексты |

Используется **Interactive Server** render mode — все компоненты через SignalR.

---

## 12. Конфигурация

### appsettings.json

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=tiktokeco;Username=app;Password=secret"
  },
  "TikTokApi": {
    "AccessToken": ""
  },
  "Pipeline": {
    "IntervalMinutes": 60
  },
  "ScoringCache": {
    "TtlMinutes": 30
  }
}
```

### Полная таблица параметров

| Параметр | По умолчанию | Описание |
|---|---|---|
| `ConnectionStrings:Default` | см. выше | Строка подключения к PostgreSQL |
| `TikTokApi:AccessToken` | `""` | RapidAPI ключ |
| `Pipeline:IntervalMinutes` | `60` | Интервал автозапуска |
| `ScoringCache:TtlMinutes` | `30` | TTL кэша правил скоринга |
| `maxPerQuery` (AppSettings) | `50` | Целевых видео на запрос |
| `anthropic:apiKey` (AppSettings) | — | Ключ Claude API |

---

## 13. Диагностические логи: полная схема

Все логи пишутся в `Console`. В Docker: `docker compose logs -f pipeline`.

### Поиск видео (SearchVideosAsync)

```
[SEARCH] Page {n}/{max} offset={offset}
[SEARCH PAGE] firstId={id} lastId={id}
[SEARCH OK] Page {n}: raw={count} new={count} baseStep={n} offsetNext={offset} cursor={c} has_more={h} [both ignored]
[SEARCH DUP] Page {n}: all {count} items already seen (duplicatePageStreak={n}/{limit})
[SEARCH EMPTY] Page {n}: itemList null/empty (emptyPageStreak={n}/{limit}) offsetNext={offset}
[SEARCH WARN] lowYieldStreak={n}: consistently low new-video yield per page
[SEARCH END] repeated duplicate pages (duplicatePageStreak={n}) — stopping. keyword="{kw}"
[SEARCH END] repeated empty pages (emptyPageStreak={n}) — stopping. keyword="{kw}"
[SEARCH END] max pages reached ({n}) — stopping. keyword="{kw}"
```

### CollectionPipeline

```
[SEARCH] Querying: "{keyword}" (type: keyword)
[SEARCH] Цель: набрать {n} видео, прошедших порог BY≥{t} ECO≥{t}
[FILTER] Видео не старше: {date}
[SKIP] Видео {id} старше DateFrom ({date})
[{passed}/{max}] [{total}] BY={score} ECO={score} | @{user}: {desc}...
[DONE] Цель достигнута: {n}/{max} видео для "{kw}" (fetched={n}, scored={n}). Остановка.
[STATS] "{kw}": fetched={n} skipped(ad/private)={n} skipped(date)={n} scored={n} passed={n}/{max}
[SCORES] Прошли порог 0/{n}. Лучшие:
    BY={score} ECO={score} [✗] @{user}: {desc}...
```

### Классификатор

```
[CLASSIFIER] {count} comments → {n} batch(es), model={model}
[CLASSIFIER] Sending batch of {n} comments to {model}...
[CLASSIFIER PARSE] Parsed {n} result(s).
[CLASSIFIER] Anthropic API key is empty. Установите ключ в разделе Settings.
[CLASSIFIER HTTP ERROR] {message}
[CLASSIFIER PARSE ERROR] {message}
```

---

## 14. Известные ограничения API

### RapidAPI TikTok (tiktok-api23.p.rapidapi.com)

1. **Малый пул результатов поиска.** Для большинства ключевых слов API возвращает всего ~30–50 уникальных видео. При любом offset выше ~300 начинаются стабильные дубли — это ограничение самого RapidAPI.

2. **`cursor` зависает.** Поле `cursor` часто остаётся неизменным (`cursor=12`) на 10–20 страницах подряд. Игнорируется.

3. **`has_more` некорректен.** `has_more=0` не означает отсутствие данных. Игнорируется.

4. **Эндпоинт `/api/comment/list` удалён.** Комментарии только через `/api/post/comments`.

5. **`202/204` при нагрузке.** Реализован ретрай с экспоненциальной выдержкой: 5s → 10s → 15s → сдаться.

### Следствия для результатов

Малый пул + жёсткий `DateFrom` → большой процент SKIP:

```
[STATS] "экология": fetched=39 skipped(date)=39 passed=0/50
```

**Решение:** смягчить `DateFrom` (например, `-90 дней`) или добавить больше разнообразных ключевых слов в `Queries`.
