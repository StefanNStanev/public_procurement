# Обществени поръчки – активни търгове в България и Европа

Агрегатор на активните обществени поръчки от **ЦАИС ЕОП** (България) и **TED** (ЕС),
с филтриране по вид поръчка, вид процедура, насока (CPV раздел), предмет (CPV код),
община (България) и държава + град (Европа).

| Част | Технологии |
|---|---|
| Бекенд | ASP.NET Core 9, EF Core 9 + Pomelo MySQL, фонова синхронизация |
| База | MySQL 8.4 |
| Фронтенд | React 19, TypeScript, Vite, axios, react-router |

## Стартиране с един клик

**Windows:** двоен клик на **`start.bat`**. Скриптът:
1. проверява за Docker, .NET 9 SDK и Node.js (и стартира Docker Desktop, ако е спрян);
2. пуска MySQL в Docker (порт 3307) и изчаква да е готов;
3. пита веднъж за Anthropic API ключ за AI анализа (може да се пропусне);
4. инсталира npm пакетите първия път;
5. отваря API-то и уеб интерфейса в два отделни прозореца и отваря http://localhost:5173.

Спиране: **`stop.bat`** (спира API, уеб и MySQL; данните остават).

**macOS/Linux:** `./start.sh`, спиране с Ctrl+C.

## Пускане изцяло в Docker

```bash
ADMIN_API_KEY=моят-ключ docker compose up --build
```

- Уеб: http://localhost:8081
- API + Swagger: http://localhost:8080/swagger

Първата синхронизация започва ~5 секунди след старта на API-то:
ЦАИС ЕОП зарежда последните 60 дни, TED – всички активни обявления (няколко минути).

## Локална разработка

```bash
# 1) MySQL (на хост порт 3307, за да не конфликтира с локален MySQL на 3306)
docker compose up -d db

# 2) API (http://localhost:5080)
cd backend/Porachki.Api
dotnet run

# 3) Фронтенд (http://localhost:5173, проксира /api към 5080)
cd frontend
npm install
npm run dev
```

Схемата се създава автоматично (`EnsureCreated`). За продукция преминете към миграции:

```bash
dotnet tool install -g dotnet-ef
dotnet ef migrations add Initial
```
и заменете `EnsureCreatedAsync()` с `MigrateAsync()` в `Program.cs`.

## Източници на данни

**ЦАИС ЕОП** – официалните дневни отворени данни в OCDS формат:
`https://storage.eop.bg/open-data-YYYY-MM-DD/`. Четецът (`Sync/Eop/EopOcdsSource.cs`)
открива файловете от S3 или HTML листинг и поддържа JSON, ZIP и GZIP, release и record пакети.
Ако структурата на хранилището се окаже различна, сменете
`Sync:Eop:DailyFolderUrlTemplate` (може да сочи директно към файл, `{date}` = `yyyy-MM-dd`).

**TED** – Search API v3 (`POST https://api.ted.europa.eu/v3/notices/search`), без ключ,
`scope=ACTIVE`, ITERATION пагинация. Заявката и полетата са в `Sync:Ted` в `appsettings.json`.
При грешка 400 TED връща името на непознатото поле – махнете го от `Fields`.

## Как се определят филтрите

| Филтър | България (ЦАИС ЕОП) | Европа (TED) |
|---|---|---|
| Вид поръчка | `tender.mainProcurementCategory` | `contract-nature` |
| Вид процедура | `tender.procurementMethodDetails` (на български) | `procedure-type` (преведен) |
| Насока | първите 2 цифри на основния CPV код | същото |
| Предмет | основен CPV код + текстово търсене | същото |
| Местоположение | община на възложителя* | държава + град на възложителя |

\* Общината се определя последователно по: името на възложителя („Община X“, „Столична община“),
пощенския код и накрая населеното място + областта (NUTS3). Използва ЕКАТТЕ данни от
[Kostadin/Places-in-Bulgaria](https://github.com/Kostadin/Places-in-Bulgaria) (MIT),
вградени в `Data/Seed/settlements.json`. Неразпознатите са в групата „Неопределена“.

**Активна** = статусът не е complete/cancelled/unsuccessful **и** крайният срок не е изтекъл
(поръчки без срок се показват 45 дни след публикуване). Изтеклите се изтриват след 30 дни.

Стойностите се нормализират в евро (BGN по фиксирания курс 1.95583); филтърът по стойност
и сортирането използват тази колона.

## AI оценка на поръчка (само ЦАИС ЕОП)

Всяка българска поръчка има бутон **„AI анализ“** (на реда, под стойността). Анализът се пуска
**само при натискане** – отварянето на поръчка показва единствено вече запазен резултат,
а прекъснат от рестарт анализ не се пуска автоматично отново. При натискане бутонът:

1. взема списъка с документи на поръчката (и на обособените ѝ позиции) от публичната услуга на
   app.eop.bg (`GetPublishedTenderDetails`, `GetSignedUrlByDocumentId`);
2. сваля файловете и извлича текста локално – PDF, DOCX, XLSX, ODT/ODS, RTF, DOC/XLS (приблизително),
   ZIP/RAR/7z и подписани `.p7m`; сканираните PDF-и (до 3) се подават на модела като документ;
3. изпраща документите на Claude (`claude-sonnet-5-5`) със structured output и връща:
   себестойност с разбивка по пера и основание, диапазон, препоръчителна оферта, марж и печалба,
   оценка по обособени позиции, рискове, ключови изисквания и неустойки.

Оценката е по средни пазарни цени в България, без данни за конкретна фирма. Резултатът се пази
в таблица `tender_estimates` – повторното отваряне е безплатно, „Нов AI анализ“ прави нова заявка.

**API ключ** (https://platform.claude.com → API Keys):

```bash
cd backend/Porachki.Api
dotnet user-secrets set "Estimates:AnthropicApiKey" "sk-ant-..."
```
С Docker: `ANTHROPIC_API_KEY=sk-ant-... docker compose up --build`.

Настройки в `Estimates` (`appsettings.json`): `Model`, `MaxTextChars` (колко текст се подава –
главният фактор за цената), `MaxFiles`, `MaxTotalMb`, `MaxNativePdfs`, `MaxEstimatesPerDay`
(предпазител срещу неволни разходи). Цената на анализ е обикновено $0,10–2 според обема документация.

Услугата на ЦАИС ЕОП не е официално документирано API – методите са взети от
[отворения код на системата](https://github.com/governmentbg/CAIS-EOP). Ако документите
са в папки или изискват регистрация, анализът ще ги отбележи като пропуснати.

## API

| Метод | Път | Описание |
|---|---|---|
| GET | `/api/{bg\|eu}/tenders` | Списък (филтри + `sort`, `page`, `pageSize`) |
| GET | `/api/{bg\|eu}/tenders/facets` | Броячи за всеки филтър |
| GET | `/api/{bg\|eu}/tenders/{id}` | Детайли с описание и всички CPV кодове |
| GET | `/api/sync/status` | Състояние на синхронизацията |
| POST | `/api/sync/{bg\|eu}/run` | Ръчно пускане (заглавка `X-Admin-Key`) |
| GET | `/api/bg/tenders/{id}/estimate` | Последната AI оценка (404, ако няма) |
| POST | `/api/bg/tenders/{id}/estimate?force=true` | Стартира оценка (202) – резултатът се проверява с GET |

Масивите се подават като повторени параметри: `?divisions=45&divisions=72`.

Типизиран клиент от Swagger (по избор, изисква Java):

```bash
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost:5080/swagger/v1/swagger.json \
  -g typescript-axios -o frontend/src/api/generated
```

## Настройки (`appsettings.json` → `Sync`)

| Ключ | По подразбиране | |
|---|---|---|
| `Eop:IntervalHours` | 6 | честота на обновяване от ЦАИС ЕОП |
| `Eop:BackfillDays` | 60 | колко дни назад при първо зареждане |
| `Ted:IntervalHours` | 2 | честота на обновяване от TED |
| `Ted:Languages` | `bul`, `eng` | предпочитан език за заглавията от TED |
| `RetentionDays` | 30 | изтриване на изтекли поръчки |
| `AdminApiKey` | `change-me` | ключ за ръчно пускане |

## Известни неизвестни

- Бекендът е написан без достъп до .NET компилатор – първият `dotnet build` може да покаже дребни грешки.
- Точните имена на файловете в `storage.eop.bg` не са потвърдени; кодът е толерантен към формата на листинга.
- Полетата `buyer-city` и `deadline-receipt-tender-date-lot` в TED не са проверени срещу реален отговор.
- Анонимният достъп до `GetSignedUrlByDocumentId` в ЦАИС ЕОП не е проверен от средата, в която е писан кодът.
