using System.Text.Json.Nodes;

namespace Porachki.Api.Estimates;

public static class EstimatePrompt
{
    public static string System(DateOnly today) => $"""
        Ти си независим консултант по обществени поръчки в България с опит в ценообразуването на
        строителство (количествено-стойностни сметки, СЕК), доставки, ИТ и услуги. Днес е {today:dd.MM.yyyy}.

        Задача: по документацията на поръчката оцени колко би струвало изпълнението ѝ за типичен
        български изпълнител при текущите пазарни цени в България, каква цена на офертата е разумна
        и каква печалба би донесла. Не разполагаш с данни за конкретна фирма – приеми средни пазарни
        ставки за труд, материали, механизация, транспорт, подизпълнители и режийни.

        Правила:
        - Всички суми са в евро (EUR) без ДДС. България е в еврозоната от 01.01.2026 г.; ако
          документацията е в лева, превърни по фиксирания курс 1,95583 лв. = 1 €.
        - Опирай се на количествата, сроковете и изискванията от документите. Когато посочваш
          основание (basis), цитирай документа и раздела/позицията, от които идва числото.
        - Когато нещо липсва, направи разумно допускане и го запиши в assumptions; не измисляй
          факти, които ги няма в документите. Посочи липсващата информация в missingInformation.
        - Включи и разходите по участието и изпълнението: гаранции за участие/изпълнение (цената
          на банкова гаранция или блокирани средства), застраховки, задължителен персонал,
          сертификати, транспорт, гаранционно обслужване.
        - Препоръчителната цена съобрази с прогнозната стойност на възложителя (ако е посочена),
          критерия за възлагане (най-ниска цена или оптимално съотношение) и очакваната конкуренция.
        - Ако поръчката има обособени позиции, дай оценка за всяка в lots, а общите суми – за всички.
        - Рисковете оценявай реалистично: неустойки, кратки срокове, твърди цени при инфлация,
          отложено плащане, неясни количества, изисквания, които стесняват кръга участници.
        - Пиши на български, кратко и конкретно.
        """;

    /// <summary>JSON схема на отговора (structured outputs).</summary>
    public static JsonObject Schema()
    {
        static JsonObject Str() => new() { ["type"] = "string" };
        static JsonObject Num() => new() { ["type"] = "number" };
        static JsonObject NullableNum() => new() { ["type"] = new JsonArray("number", "null") };
        static JsonObject NullableStr() => new() { ["type"] = new JsonArray("string", "null") };
        static JsonObject OneOf(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };
        static JsonObject Obj(params (string Name, JsonObject Schema)[] props)
        {
            var p = new JsonObject();
            // Всеки JsonNode може да има само един родител – при повторна употреба се клонира
            foreach (var (name, schema) in props) p[name] = schema.Parent is null ? schema : schema.DeepClone();
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = p,
                ["required"] = new JsonArray(props.Select(x => (JsonNode)x.Name).ToArray()),
                ["additionalProperties"] = false,
            };
        }
        static JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item.Parent is null ? item : item.DeepClone() };

        var costLine = Obj(
            ("category", Str()),      // Материали, Труд, Механизация, Подизпълнители, Лицензи, Гаранции, Режийни…
            ("description", Str()),
            ("amount", Num()),
            ("basis", Str()));        // откъде идва числото

        var bid = Obj(
            ("price", Num()),
            ("marginPercent", Num()),
            ("profit", Num()),
            ("rationale", Str()));

        var lot = Obj(
            ("name", Str()),
            ("estimatedValue", NullableNum()),
            ("totalCost", Num()),
            ("recommendedBid", Num()),
            ("profit", Num()));

        static JsonObject Note() => Obj(("title", Str()), ("description", Str()));

        return Obj(
            ("summary", Str()),
            ("currency", Str()),
            ("estimatedValueFromDocs", NullableNum()),
            ("awardCriterion", NullableStr()),
            ("executionPeriod", NullableStr()),
            ("paymentTerms", NullableStr()),
            ("costBreakdown", Arr(costLine)),
            ("totalCost", Num()),
            ("costRange", Obj(("low", Num()), ("high", Num()))),
            ("recommendedBid", bid),
            ("lots", Arr(lot)),
            ("risks", Arr(Obj(("title", Str()), ("description", Str()), ("severity", OneOf("low", "medium", "high"))))),
            ("requirements", Arr(Note())),
            ("penalties", Arr(Note())),
            ("assumptions", Arr(Str())),
            ("missingInformation", Arr(Str())),
            ("confidence", OneOf("low", "medium", "high")),
            ("confidenceReason", Str()));
    }

    /// <summary>Използва се, ако API-то не приеме structured outputs – описва формата с думи.</summary>
    public const string JsonFallbackInstruction = """

        Върни САМО валиден JSON обект (без markdown) със следните полета:
        summary, currency ("EUR"), estimatedValueFromDocs (число или null), awardCriterion, executionPeriod,
        paymentTerms (низ или null), costBreakdown [{category, description, amount, basis}], totalCost,
        costRange {low, high}, recommendedBid {price, marginPercent, profit, rationale},
        lots [{name, estimatedValue, totalCost, recommendedBid, profit}],
        risks [{title, description, severity: low|medium|high}], requirements [{title, description}],
        penalties [{title, description}], assumptions [низ], missingInformation [низ],
        confidence (low|medium|high), confidenceReason.
        """;
}
