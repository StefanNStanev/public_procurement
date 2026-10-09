using System.Text.Json.Nodes;

namespace Porachki.Api.Estimates;

/// <summary>Инструкции и JSON схема за анализа „Стъпки за изпълнение“.</summary>
public static class RoadmapPrompt
{
    public static string System(DateOnly today) => $"""
        Ти си консултант по обществени поръчки в България с практически опит в подготовката на оферти
        през ЦАИС ЕОП и в изпълнението на договори по Закона за обществените поръчки (ЗОП). Днес е {today:dd.MM.yyyy}.

        Задача: по документацията на поръчката състави практичен план за участник (икономически оператор):
        1) предпоставки – всичко, което трябва да е налично или уредено ПРЕДИ подаването на офертата;
        2) стъпките от подготовката и подаването на документите за участие, през оценката, сключването
           на договора и изпълнението, до приемането, окончателното плащане и освобождаването на гаранциите.

        Правила:
        - Дати, срокове, изисквания и документи вземай от документацията и посочвай източника (source):
          име на документа и раздел/точка. Не измисляй дати: ако срок не е посочен, date е null,
          а липсата се записва в missingInformation.
        - Ако стъпка или изискване не идва от документацията, а от обичайната практика по ЗОП/ППЗОП,
          отбележи го в описанието с „(общо правило по ЗОП)“. Не цитирай номера на членове от закона,
          освен ако не са посочени в самата документация.
        - Предпоставките включват според случая: регистрация в ЦАИС ЕОП, квалифициран електронен подпис
          (КЕП), ЕЕДОП, липса на основания за отстраняване, критерии за подбор (оборот, опит, персонал,
          сертификати, вписвания в регистри, лицензи), гаранции, застраховки, обединения и подизпълнители,
          оглед на място, искане на разяснения. За всяка посочи дали е задължителна и колко време
          обикновено отнема набавянето ѝ (leadTime), напр. „банкова гаранция: 3–7 работни дни“.
        - Стъпките подреди хронологично и ги разпредели по фази. За всяка посочи кой действа
          (участникът, възложителят или двамата), кога (timing) и какви документи са нужни.
        - В commonPitfalls изброй конкретните грешки, които при тази поръчка най-често водят до
          отстраняване или неустойки.
        - Пиши на български, кратко и конкретно, като за човек, който подготвя офертата.
        """;

    public static readonly string[] PrerequisiteCategories = ["registration", "eligibility", "economic", "technical", "guarantee", "other"];
    public static readonly string[] Phases = ["preparation", "submission", "evaluation", "contract", "execution", "completion"];

    public static JsonObject Schema()
    {
        static JsonObject Str() => new() { ["type"] = "string" };
        static JsonObject Bool() => new() { ["type"] = "boolean" };
        static JsonObject NullableStr() => new() { ["type"] = new JsonArray("string", "null") };
        static JsonObject OneOf(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };
        static JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item.Parent is null ? item : item.DeepClone() };
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

        var keyDate = Obj(
            ("label", Str()),          // „Краен срок за оферти“, „Срок за разяснения“, „Срок за изпълнение“…
            ("date", NullableStr()),   // както е в документацията: дата/час или период
            ("note", NullableStr()));

        var prerequisite = Obj(
            ("category", OneOf(PrerequisiteCategories)),
            ("title", Str()),
            ("description", Str()),
            ("mandatory", Bool()),
            ("leadTime", NullableStr()),
            ("source", NullableStr()));

        var step = Obj(
            ("phase", OneOf(Phases)),
            ("title", Str()),
            ("description", Str()),
            ("responsible", OneOf("participant", "buyer", "both")),
            ("timing", NullableStr()),
            ("documents", Arr(Str())),
            ("tip", NullableStr()));

        return Obj(
            ("summary", Str()),
            ("keyDates", Arr(keyDate)),
            ("prerequisitesOverview", Str()),
            ("prerequisites", Arr(prerequisite)),
            ("steps", Arr(step)),
            ("commonPitfalls", Arr(Str())),
            ("missingInformation", Arr(Str())));
    }

    /// <summary>Използва се, ако API-то не приеме structured outputs.</summary>
    public const string JsonFallbackInstruction = """

        Върни САМО валиден JSON обект (без markdown) със следните полета:
        summary, keyDates [{label, date (низ или null), note (низ или null)}],
        prerequisitesOverview (абзац), prerequisites [{category: registration|eligibility|economic|technical|guarantee|other,
        title, description, mandatory (true/false), leadTime (низ или null), source (низ или null)}],
        steps [{phase: preparation|submission|evaluation|contract|execution|completion, title, description,
        responsible: participant|buyer|both, timing (низ или null), documents [низ], tip (низ или null)}],
        commonPitfalls [низ], missingInformation [низ].
        """;
}
