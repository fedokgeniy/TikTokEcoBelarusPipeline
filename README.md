# TikTokEcoBelarusPipeline

> ASP.NET Core + Blazor приложение для автоматического сбора, скоринга и хранения TikTok-видео по эко-тематике Беларуси.

---

## Содержание

- [Назначение](#назначение)
- [Стек технологий](#стек-технологий)
- [Архитектура](#архитектура)
- [Структура проекта](#структура-проекта)
- [Ключевые компоненты](#ключевые-компоненты)
- [База данных](#база-данных)
- [Скоринг](#скоринг)
- [Веб-интерфейс](#веб-интерфейс)
- [Запуск](#запуск)
- [Конфигурация](#конфигурация)
- [Известные ограничения](#известные-ограничения)

---

## Назначение

Проект собирает TikTok-видео по заданным поисковым запросам (ключевые слова и хэштеги), оценивает каждое видео по двум осям:

- **BelarusScore** — насколько видео связано с Беларусью (географические названия, культура, авторы)
- **EcoScore** — насколько видео связано с экологической тематикой

Видео, прошедшие оба порога, сохраняются в PostgreSQL и отображаются в Blazor-интерфейсе.

---

## Стек технологий

| Компонент | Технология |
|---|---|
| Фреймворк | ASP.NET Core 9, Blazor SSR |
| БД | PostgreSQL 16 (Docker) |
| ORM | Entity Framework Core |
| API | TikTok API via RapidAPI (`tiktok-api23`) |
| UI | Blazor Components (`.razor`) |
| Контейнеризация | Docker Compose |

---

## Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                    Blazor Web UI                         │
│   /  /videos  /pipeline  /queries  /scoring  /export    │
└──────────────────────┬──────────────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────────────┐
│                 PipelineOrchestrator                     │
│  (запуск по кнопке или таймеру, координация всего потока)│
└──────┬───────────────┬──────────────────────────────────┘
       │               │
┌──────▼──────┐ ┌──────▼────────────────────┐
│CollectionPi │ │   VideoDeduplicationService │
│peline       │ │   (проверка дублей по VideoId)│
│             │ └─────────────────────────────┘
│ ┌─────────┐ │
│ │TikTokApi│ │        ┌──────────────────────┐
│ │Client   │ │        │   BelarusEcoScorer    │
│ └────┬────┘ │        │  (правила из БД,      │
│      │      ├───────►│   пороги minBY/minECO)│
│      │API   │        └──────────────────────┘
│      ▼      │
│  RapidAPI   │        ┌──────────────────────┐
│  ~23 видео  │        │   CsvExportService    │
│  на запрос  │        │  (экспорт результатов)│
└─────────────┘        └──────────────────────┘
                                │
               ┌────────────────▼──────────────┐
               │         PostgreSQL             │
               │  Videos, SearchQueries,        │
               │  ScoringRules, AppSettings,    │
               │  VideoSearchQueryLinks         │
               └───────────────────────────────┘
```

---

## Структура проекта

```
TikTokEcoBelarusPipeline/
├── Program.cs                        # DI, EF, маршруты, seed данных
├── appsettings.json                  # строка подключения, RapidAPI ключ
├── docker-compose.yml                # PostgreSQL контейнер
│
├── Pipeline/
│   └── CollectionPipeline.cs         # основной цикл сбора и фильтрации
│
├── Services/
│   ├── TikTokApiClient.cs            # HTTP-клиент RapidAPI, пагинация
│   ├── BelarusEcoScorer.cs           # скоринг по правилам из БД
│   ├── PipelineOrchestrator.cs       # координация: запуск, сохранение, экспорт
│   ├── VideoDeduplicationService.cs  # дедупликация по VideoId
│   └── CsvExportService.cs           # экспорт в CSV
│
├── Domain/
│   └── (доменные модели, интерфейсы)
│
├── Infrastructure/
│   ├── AppDbContext.cs               # EF DbContext
│   ├── Repositories/                 # репозитории для каждой сущности
│   └── Migrations/                   # EF миграции
│
├── Models/
│   ├── TikTokSearchResponse.cs       # десериализация ответа RapidAPI
│   ├── ScoredVideo.cs                # результат скоринга
│   ├── SearchQuery.cs                # поисковый запрос
│   ├── ScoringRule.cs                # правило скоринга
│   └── Video.cs                      # сущность видео в БД
│
└── Components/
    └── Pages/                        # Blazor-страницы
        ├── Index.razor               # дашборд
        ├── Videos.razor              # список видео с фильтрами
        ├── Pipeline.razor            # запуск пайплайна, настройки порогов
        ├── Queries.razor             # управление поисковыми запросами
        ├── Scoring.razor             # управление правилами скоринга
        └── Export.razor              # экспорт CSV
```

---

## Ключевые компоненты

### TikTokApiClient

Обращается к `tiktok-api23.p.rapidapi.com/api/search/video` с параметрами `keyword`, `cursor`, `search_id`.

**Важные особенности:**
- API возвращает **ровно 23 видео** на запрос (12 на странице 1 + 11 на странице 2), после чего `has_more=0`
- Параметр `maxPages` в `SearchVideosAsync` — это лимит страниц, не видео
- `search_id` берётся из `response.Extra.SearchRequestId` для корректной пагинации
- Задержка между страницами: 1500ms по умолчанию

### CollectionPipeline

Центральный цикл сбора. Для каждого активного `SearchQuery`:
1. Вызывает `TikTokApiClient.SearchVideosAsync`
2. Пропускает рекламные и приватные аккаунты
3. Фильтрует по `DateFrom` если задано
4. Скорит каждое видео через `BelarusEcoScorer`
5. Логирует статистику `[STATS]` и топ-5 скоров при `passed=0`

### BelarusEcoScorer

Скорит видео на основе правил `ScoringRule` из БД. Каждое правило — это ключевое слово с весом, категорией (`belarus`/`eco`) и контекстом поиска (`description`, `hashtags`, `author_bio`). Пороги `minBelarus` и `minEco` хранятся в таблице `AppSettings`.

**Формула:**
```
BelarusScore = Σ(weight_i) для всех совпадений belarus-правил / maxPossibleScore
EcoScore     = Σ(weight_i) для всех совпадений eco-правил / maxPossibleScore
TotalScore   = (BelarusScore + EcoScore) / 2
```

Правила поддерживают `MaxMatches` — ограничение на количество учитываемых совпадений одного правила.

### PipelineOrchestrator

Запускается из Blazor UI кнопкой или по расписанию. Последовательность:
1. Читает `minBelarus`, `minEco`, `maxPerQuery` из `AppSettings`
2. Запускает `CollectionPipeline.RunAsync`
3. Дедуплицирует результаты через `VideoDeduplicationService`
4. Сохраняет новые видео и связи `VideoSearchQueryLinks`
5. Опционально экспортирует в CSV

---

## База данных

| Таблица | Назначение |
|---|---|
| `Videos` | Сохранённые видео со всеми метаданными и скорами |
| `SearchQueries` | Поисковые запросы (keyword/hashtag, DateFrom, Priority) |
| `VideoSearchQueryLinks` | M2M: какой запрос нашёл какое видео |
| `ScoringRules` | Правила скоринга с весами |
| `ScoringRuleThresholds` | Бонусные пороги (если N совпадений — +bonus) |
| `AppSettings` | Настройки пайплайна: minBelarus, minEco, maxPerQuery |

**Важно:** значения в `AppSettings` хранятся как строки. Разделитель дробной части — **точка** (`.`). При сохранении используется `InvariantCulture`.

---

## Скоринг

Правила скоринга управляются через страницу `/scoring` и хранятся в БД. Пример правил:

| Keyword | Category | Context | Weight |
|---|---|---|---|
| беларусь | belarus | hashtags | 0.4 |
| минск | belarus | description | 0.3 |
| природа | eco | description | 0.3 |
| экология | eco | hashtags | 0.5 |

Эффективные запросы — комбинированные, типа `"природа минск"` — дают конверсию ~22% (5/23). Одиночные общие слова (`"лес"`, `"весна"`) дают 0% из-за отсутствия географической привязки.

---

## Веб-интерфейс

| Страница | Описание |
|---|---|
| `/` | Дашборд: кол-во видео, средние скоры, топ авторов |
| `/videos` | Таблица всех видео с пагинацией, сортировкой и фильтрами |
| `/pipeline` | Запуск пайплайна, настройка порогов и `maxPerQuery` |
| `/queries` | CRUD поисковых запросов, включение/отключение |
| `/scoring` | CRUD правил скоринга и порогов |
| `/export` | Экспорт текущей выборки в CSV |

---

## Запуск

### 1. Запустить PostgreSQL

```bash
docker-compose up -d
```

`docker-compose.yml` поднимает PostgreSQL 16 с:
- `POSTGRES_USER=app`
- `POSTGRES_PASSWORD=secret`
- `POSTGRES_DB=tiktokeco`

### 2. Настроить `appsettings.json`

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=tiktokeco;Username=app;Password=secret"
  },
  "RapidApi": {
    "Key": "YOUR_RAPIDAPI_KEY"
  }
}
```

### 3. Запустить приложение

```bash
dotnet run
# или через Visual Studio / Rider
```

При первом запуске EF автоматически применяет миграции и заполняет таблицы seed-данными (правила скоринга, поисковые запросы).

### 4. Открыть браузер

```
https://localhost:65065
```

---

## Конфигурация

### AppSettings (в БД, страница `/pipeline`)

| Ключ | Описание | По умолчанию |
|---|---|---|
| `minBelarus` | Минимальный BelarusScore для сохранения | `0.20` |
| `minEco` | Минимальный EcoScore для сохранения | `0.20` |
| `maxPerQuery` | Максимум страниц API на один запрос | `5` |

> ⚠️ Значения хранятся с точкой (`.`) как разделителем. Если значения записаны с запятой, выполнить:
> ```sql
> UPDATE "AppSettings" SET "Value" = replace("Value", chr(44), chr(46))
> WHERE "Key" IN ('minBelarus', 'minEco');
> ```

---

## Известные ограничения

- **API лимит:** `tiktok-api23` возвращает максимум 23 видео на запрос (2 страницы). `maxPerQuery > 2` не даёт дополнительных результатов для большинства запросов.
- **Качество запросов:** одиночные слова без географической привязки дают 0% конверсию. Рекомендуются запросы вида `"<эко-тема> <город/регион Беларуси>"`.
- **Культура чисел:** настройки порогов зависят от `InvariantCulture`. При смене локали сервера возможно повторное появление бага с запятой.
