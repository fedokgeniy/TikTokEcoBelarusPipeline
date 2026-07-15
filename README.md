# TikTok Eco Belarus Pipeline

> **ASP.NET Core 10 · Blazor Server · PostgreSQL 16 · EF Core 9 · Docker**

Автоматизированный pipeline для сбора, двойной оценки и мониторинга TikTok-контента, связанного с экологией Беларуси. Приложение работает в Blazor Server и управляется через веб-интерфейс без каких-либо внешних инструментов.

---

## Архитектура

```
TikTokEcoBelarusPipeline/
├── Components/          # Blazor Server UI (Pages + Layout)
│   └── Pages/
│       ├── Dashboard.razor      — сводная статистика
│       ├── Pipeline.razor       — запуск / прогресс пайплайна
│       ├── Videos.razor         — таблица отобранных видео
│       ├── Channels.razor       — трекинг каналов
│       ├── Comments.razor       — комментарии к видео
│       ├── Hashtags.razor       — хэштег-аналитика
│       ├── Queries.razor        — управление поисковыми запросами
│       └── Scoring.razor        — настройка правил скоринга
├── Domain/
│   └── Entities/        # EF Core-сущности: Video, SearchQuery, ScoringRule,
│                        #   TrackedChannel, TrackedChannelVideo, VideoComment, …
├── Infrastructure/
│   ├── AppDbContext.cs
│   ├── SeedData.cs      # Автосид правил скоринга и поисковых запросов
│   ├── Configurations/  # Fluent API конфигурации
│   └── Repositories/    # IScoringRuleRepository, ISearchQueryRepository,
│                        #   ITrackedChannelRepository
├── Pipeline/
│   ├── CollectionPipeline.cs    — постраничный сбор видео по ключевым словам
│   └── ChannelMonitorPipeline.cs — мониторинг каналов на появление новых видео
├── Services/
│   ├── TikTokApiClient.cs       — клиент RapidAPI (async IAsyncEnumerable)
│   ├── BelarusEcoScorer.cs      — двойной скоринг BY + ECO с кэшем правил
│   ├── PipelineOrchestrator.cs  — оркестратор: запуск, сохранение в БД, CSV
│   ├── CsvExportService.cs      — экспорт результатов через CsvHelper
│   └── VideoDeduplicationService.cs
├── Models/              # DTO / модели ответов TikTok API
├── Migrations/          # EF Core migrations
├── Program.cs
├── appsettings.json
└── docker-compose.yml
```

---

## Как работает pipeline

### 1. CollectionPipeline — сбор по ключевым словам

1. Читает активные `SearchQuery` из БД (отсортированы по `Priority`).
2. Постранично запрашивает `TikTokApiClient.SearchVideosAsync` (async stream).
3. Каждое видео проходит через `BelarusEcoScorer`:
   - **BelarusScore** — сумма весовых совпадений по категориям: `Belarus_Explicit`, `Belarus_City`, `Belarus_Place`, `Belarus_Language`, флаг 🇧🇾 в bio (+0.35), verified-бонус (+0.10).
   - **EcoScore** — аналогичная логика по эко-ключевым словам.
4. Видео сохраняется, только если `BelarusScore ≥ minBelarus` **И** `EcoScore ≥ minEco`.
5. Остановка при наборе `maxVideos` прошедших видео на запрос (или `maxPagesHardLimit = 100` страниц).

### 2. ChannelMonitorPipeline — мониторинг каналов

1. Итерируется по активным `TrackedChannel`.
2. Запрашивает `/api/user/info` → получает актуальный `videoCount`.
3. Если `videoCount > LastVideoCount` → вычисляет дельту и тянет последние видео.
4. Сохраняет только те `VideoId`, которых ещё нет в `TrackedChannelVideos`.
5. Для каждого нового видео загружает последние комментарии → `VideoComments`.
6. Обновляет `LastVideoCount` канала.

### 3. PipelineOrchestrator

Singleton-сервис, запускаемый из Blazor UI:
- Берёт `maxPerQuery` из таблицы `AppSettings` (дефолт — 50).
- Сохраняет видео в `Videos`, дедуплицирует через `VideoSearchQueryLink`.
- Экспортирует результаты в CSV.
- Возвращает `PipelineRunResult` с метриками `Found / Saved / Skipped / Duration`.

---

## Скоринг-система

`BelarusEcoScorer` полностью конфигурируется через БД. Правила и пороговые значения загружаются через `IScoringRuleRepository` и кэшируются в `IMemoryCache` (TTL — `ScoringCache:TtlMinutes`, по умолчанию 30 мин).

### Категории BelarusScore (из SeedData)

| Категория | Примеры ключевых слов | Weight | MaxMatches |
|---|---|---|---|
| `Belarus_Explicit` | беларусь, belarus, bielarus, 🇧🇾, by | 0.40 | 1 |
| `Belarus_City` | минск, minsk, гродно, брест, витебск, могилев, гомель | 0.25 | 1 |
| `Belarus_Place` | беловежская, нарочь, налибоки, припять, неман | 0.30 | 1 |
| `Belarus_Language` | прырода, экалогія, лес | 0.10 | 99 (threshold) |

Бонусы вне групп: 🇧🇾 в `author.signature` → **+0.35**, `verified` при `score > 0.2` → **+0.10**. Итоговый счёт нормируется до `[0, 1]`.

---

## База данных

Используется **PostgreSQL 16**. Миграции применяются автоматически при старте (`db.Database.MigrateAsync()`). Seed-данные добавляются один раз при первом запуске.

### Основные таблицы

| Таблица | Описание |
|---|---|
| `Videos` | Отобранные видео с оценками и метаданными |
| `SearchQueries` | Поисковые запросы с приоритетом и фильтром по дате |
| `VideoSearchQueryLinks` | M:N связь видео ↔ запрос |
| `ScoringRules` | Правила скоринга (ключевые слова, веса, контексты) |
| `ScoringRuleThresholds` | Ступенчатые пороги для категорий скоринга |
| `TrackedChannels` | Отслеживаемые TikTok-каналы |
| `TrackedChannelVideos` | Видео, найденные при мониторинге каналов |
| `VideoComments` | Комментарии к видео каналов |
| `AppSettings` | Key-value настройки (например, `maxPerQuery`) |

---

## Быстрый старт

### Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/) (для PostgreSQL)

### 1. Запустить PostgreSQL

```bash
docker-compose up -d
```

Поднимает PostgreSQL 16 на порту `5432` с базой `tiktokeco`, пользователем `app`, паролем `secret`.

### 2. Настроить строку подключения

`appsettings.json` уже содержит корректные параметры для docker-compose окружения:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=tiktokeco;Username=app;Password=secret"
  },
  "Pipeline": {
    "IntervalMinutes": 60
  },
  "ScoringCache": {
    "TtlMinutes": 30
  }
}
```

### 3. Запустить приложение

```bash
dotnet run
```

При первом запуске автоматически:
- Применяются все EF Core миграции.
- Выполняется `SeedData` — заполняются правила скоринга и базовые поисковые запросы.

Приложение будет доступно по адресу `http://localhost:5000`.

---

## Конфигурация

| Параметр | По умолчанию | Описание |
|---|---|---|
| `ConnectionStrings:Default` | см. appsettings.json | Строка подключения к PostgreSQL |
| `Pipeline:IntervalMinutes` | `60` | Интервал автозапуска пайплайна |
| `ScoringCache:TtlMinutes` | `30` | TTL кэша правил скоринга |
| `maxPerQuery` (AppSettings) | `50` | Целевое кол-во видео на поисковый запрос |
| `minBelarus` | `0.30` | Минимальный порог BelarusScore |
| `minEco` | `0.30` | Минимальный порог EcoScore |

> **Важно:** API-ключ RapidAPI задаётся в `Program.cs`. Для продакшна вынесите его в `appsettings.json` или переменные окружения.

---

## Зависимости

| Пакет | Версия | Назначение |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | 9.x | ORM |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.x | PostgreSQL-провайдер |
| `Microsoft.EntityFrameworkCore.Design` | 9.x | EF Core migrations |
| `Microsoft.Extensions.Caching.Memory` | 10.x | Кэш правил скоринга |
| `CsvHelper` | 33.1.0 | CSV-экспорт результатов |

---

## Технологический стек

- **Runtime:** .NET 10 / ASP.NET Core
- **UI:** Blazor Server (Interactive Server render mode)
- **ORM:** Entity Framework Core 9 + Npgsql
- **База данных:** PostgreSQL 16
- **Контейнеризация:** Docker / docker-compose
- **Внешний API:** TikTok via RapidAPI
