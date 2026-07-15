# TikTok Eco Belarus Pipeline

> **ASP.NET Core 10 · Blazor Server · PostgreSQL 16 · EF Core 9 · Docker**

Автоматизированный pipeline для сбора, двойной оценки и мониторинга TikTok-контента, связанного с экологией Беларуси. Приложение работает на Blazor Server и управляется через веб-интерфейс.

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
│   ├── TikTokApiClient.cs           — клиент RapidAPI с устойчивой пагинацией
│   ├── BelarusEcoScorer.cs          — двойной скоринг BY + ECO с кэшем правил
│   ├── PipelineOrchestrator.cs      — оркестратор: запуск, сохранение в БД, CSV
│   ├── CsvExportService.cs          — экспорт результатов через CsvHelper
│   ├── CommentClassifierService.cs  — классификация комментариев
│   └── VideoDeduplicationService.cs — дедупликация видео по videoId
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
2. Постранично запрашивает `TikTokApiClient.SearchVideosAsync`.
3. Каждое видео проходит через `BelarusEcoScorer`:
   - **BelarusScore** — сумма весовых совпадений по категориям: `Belarus_Explicit`, `Belarus_City`, `Belarus_Place`, `Belarus_Language`, флаг 🇧🇾 в bio (+0.35), verified-бонус (+0.10).
   - **EcoScore** — аналогичная логика по эко-ключевым словам.
4. Видео сохраняется, только если `BelarusScore ≥ minBelarus` **И** `EcoScore ≥ minEco`.
5. Остановка при наборе `maxVideos` прошедших видео на запрос (или `maxPages = 100` страниц).

### 2. Пагинация поиска (TikTokApiClient)

RapidAPI TikTok endpoint возвращает нестабильный `cursor` и ненадёжный `has_more`. Клиент использует устойчивую пагинацию на основе `offset` и стрик-счётчиков:

- `has_more` и неизменный `cursor` **игнорируются** — не используются как критерий остановки.
- Продвижение по страницам — через `offset`, увеличиваемый на фактическое количество элементов страницы.
- Дедупликация в рамках одного поискового прохода — по `videoId` через `HashSet`.

**Правила остановки:**

| Счётчик | Порог | Описание |
|---|---|---|
| `duplicatePageStreak` | ≥ 3 | Подряд страниц без новых `videoId` |
| `emptyPageStreak` | ≥ 2 | Подряд пустых страниц (нет `itemList`) |
| `maxPages` | 100 | Жёсткий предел страниц |

**Диагностические логи по каждой странице:**

```
[SEARCH] Page 7/100 offset=60
[SEARCH PAGE] firstId=7441xxxxx lastId=7388xxxxx
[SEARCH OK] Page 7: raw=10 new=4 offsetNext=70
[SEARCH DUP] Page 9: all 10 items already seen duplicatePageStreak=1/3
[SEARCH END] repeated duplicate pages (duplicatePageStreak=3) — stopping
[SEARCH END] repeated empty pages (emptyPageStreak=2) — stopping
[SEARCH END] max pages reached
```

### 3. ChannelMonitorPipeline — мониторинг каналов

1. Итерируется по активным `TrackedChannel`.
2. Запрашивает `/api/user/info` → получает актуальный `videoCount`.
3. Если `videoCount > LastVideoCount` → тянет последние видео (дельта).
4. Сохраняет только те `VideoId`, которых ещё нет в `TrackedChannelVideos`.
5. Для каждого нового видео загружает последние комментарии → `VideoComments`.
6. Обновляет `LastVideoCount` канала.

### 4. PipelineOrchestrator

Singleton-сервис, запускаемый из Blazor UI:
- Берёт `maxPerQuery` из таблицы `AppSettings` (дефолт — 50).
- Сохраняет видео в `Videos`, дедуплицирует через `VideoSearchQueryLink`.
- Экспортирует результаты в CSV через `CsvExportService`.
- Возвращает `PipelineRunResult` с метриками `Found / Saved / Skipped / Duration`.

---

## Скоринг-система

`BelarusEcoScorer` полностью конфигурируется через БД. Правила загружаются через `IScoringRuleRepository` и кэшируются в `IMemoryCache` (TTL — `ScoringCache:TtlMinutes`, по умолчанию 30 мин).

### Категории BelarusScore (из SeedData)

| Категория | Примеры ключевых слов | Weight | MaxMatches |
|---|---|---|---|
| `Belarus_Explicit` | беларусь, belarus, bielarus, 🇧🇾, by | 0.40 | 1 |
| `Belarus_City` | минск, minsk, гродно, брест, витебск, могилев, гомель | 0.25 | 1 |
| `Belarus_Place` | беловежская, нарочь, налибоки, припять, неман | 0.30 | 1 |
| `Belarus_Language` | прырода, экалогія, лес | 0.10 | 99 |

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

## Быстрый старт (локально)

### Требования

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://www.docker.com/)

### 1. Запустить PostgreSQL

```bash
docker compose up -d postgres
```

Поднимает PostgreSQL 16 на порту `5432` с базой `tiktokeco`, пользователем `app`, паролем `secret`.

### 2. Настроить API ключ

В `appsettings.json` укажи RapidAPI ключ:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=tiktokeco;Username=app;Password=secret"
  },
  "TikTokApi": {
    "AccessToken": "ВАШ_RAPIDAPI_KEY"
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

При первом запуске автоматически применяются EF Core миграции и выполняется `SeedData`. Приложение будет доступно по адресу `http://localhost:5000`.

---

## Деплой на AWS EC2

### Требования к серверу

| Параметр | Минимум | Рекомендуется |
|---|---|---|
| RAM | 2 GB | 4 GB |
| CPU | 1 vCPU | 2 vCPU |
| Диск | 10 GB | 20 GB |
| EC2 тип | `t3.small` | `t3.medium` |
| ОС | Ubuntu 22.04 LTS | Ubuntu 24.04 LTS |

> ⚠️ `t2.micro` (1 GB RAM) не подходит для сборки без swap — компилятор .NET 10 требует ≥ 1.5 GB свободной памяти.

### 1. Подключиться к серверу

```powershell
# Windows — настроить права ключа
icacls "D:\Serverkey\tiktok.pem" /inheritance:r
icacls "D:\Serverkey\tiktok.pem" /grant:r "$($env:USERNAME):(R)"

ssh -i "D:\Serverkey\tiktok.pem" ubuntu@<IP>
```

### 2. Установить Docker

```bash
sudo apt-get update && sudo apt-get upgrade -y
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker
sudo apt-get install -y docker-compose-plugin git
```

### 3. (При необходимости) Добавить swap

Если на сервере меньше 2 GB RAM:

```bash
sudo fallocate -l 2G /swapfile
sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

### 4. Клонировать репозиторий

```bash
git clone https://github.com/fedokgeniy/TikTokEcoBelarusPipeline.git
cd TikTokEcoBelarusPipeline
```

### 5. Создать Dockerfile

```bash
cat > Dockerfile << 'EOF'
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish TikTokEcoBelarusPipeline.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TikTokEcoBelarusPipeline.dll"]
EOF
```

### 6. Создать appsettings.Production.json

```bash
cat > appsettings.Production.json << 'EOF'
{
  "ConnectionStrings": {
    "Default": "Host=postgres;Port=5432;Database=tiktokeco;Username=app;Password=secret"
  },
  "TikTokApi": {
    "AccessToken": "ВАШ_RAPIDAPI_KEY"
  }
}
EOF
```

### 7. Обновить docker-compose.yml

```bash
cat > docker-compose.yml << 'EOF'
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: tiktokeco
      POSTGRES_USER: app
      POSTGRES_PASSWORD: secret
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U app -d tiktokeco"]
      interval: 5s
      retries: 5

  pipeline:
    build: .
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    depends_on:
      postgres:
        condition: service_healthy
    restart: unless-stopped

volumes:
  pgdata:
EOF
```

### 8. Запустить

```bash
# Первый запуск (~5-10 минут)
docker compose up -d --build

# Смотреть логи
docker compose logs -f pipeline
```

### Обновление после git push

```bash
cd TikTokEcoBelarusPipeline
git pull
docker compose up -d --build pipeline
```

---

## Конфигурация

| Параметр | По умолчанию | Описание |
|---|---|---|
| `ConnectionStrings:Default` | см. appsettings.json | Строка подключения к PostgreSQL |
| `TikTokApi:AccessToken` | `""` | RapidAPI ключ для TikTok API |
| `Pipeline:IntervalMinutes` | `60` | Интервал автозапуска пайплайна |
| `ScoringCache:TtlMinutes` | `30` | TTL кэша правил скоринга |
| `maxPerQuery` (AppSettings) | `50` | Целевое кол-во видео на запрос |
| `minBelarus` | `0.30` | Минимальный порог BelarusScore |
| `minEco` | `0.30` | Минимальный порог EcoScore |

---

## Зависимости

| Пакет | Назначение |
|---|---|
| `Microsoft.EntityFrameworkCore` 9.x | ORM |
| `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x | PostgreSQL-провайдер |
| `Microsoft.EntityFrameworkCore.Design` 9.x | EF Core migrations |
| `Microsoft.Extensions.Caching.Memory` 10.x | Кэш правил скоринга |
| `CsvHelper` 33.x | CSV-экспорт результатов |

---

## Технологический стек

- **Runtime:** .NET 10 / ASP.NET Core
- **UI:** Blazor Server (Interactive Server render mode)
- **ORM:** Entity Framework Core 9 + Npgsql
- **База данных:** PostgreSQL 16
- **Контейнеризация:** Docker / docker-compose
- **Внешний API:** TikTok via RapidAPI (`tiktok-api23.p.rapidapi.com`)
