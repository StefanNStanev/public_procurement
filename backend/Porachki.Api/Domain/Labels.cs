namespace Porachki.Api.Domain;

/// <summary>Етикети на български за стойностите, по които се филтрира.</summary>
public static class Labels
{
    public static readonly IReadOnlyDictionary<string, string> ContractTypes = new Dictionary<string, string>
    {
        ["goods"] = "Доставки",
        ["services"] = "Услуги",
        ["works"] = "Строителство",
    };

    /// <summary>Видове процедури в TED (eForms procedure-type) и OCDS procurementMethod.</summary>
    public static readonly IReadOnlyDictionary<string, string> Procedures = new Dictionary<string, string>
    {
        // TED / eForms
        ["open"] = "Открита процедура",
        ["restricted"] = "Ограничена процедура",
        ["neg-w-call"] = "Състезателна процедура с договаряне",
        ["neg-wo-call"] = "Договаряне без предварително обявление",
        ["comp-dial"] = "Състезателен диалог",
        ["innovation"] = "Партньорство за иновации",
        ["comp-tend"] = "Конкурс за проект",
        ["oth-single"] = "Друга едноетапна процедура",
        ["oth-mult"] = "Друга многоетапна процедура",
        // OCDS procurementMethod (когато липсва детайл)
        ["selective"] = "Процедура с подбор",
        ["limited"] = "Ограничена/договаряне",
        ["direct"] = "Пряко възлагане",
    };

    /// <summary>CPV раздели (2 цифри) – „насока“.</summary>
    public static readonly IReadOnlyDictionary<string, string> CpvDivisions = new Dictionary<string, string>
    {
        ["03"] = "Селскостопански, горски и рибни продукти",
        ["09"] = "Горива, електроенергия и други енергийни източници",
        ["14"] = "Минни продукти и неблагородни метали",
        ["15"] = "Храни, напитки и тютюн",
        ["16"] = "Селскостопанска техника",
        ["18"] = "Облекло, обувки, багаж и аксесоари",
        ["19"] = "Кожа, текстил, пластмаси и каучук",
        ["22"] = "Печатни материали",
        ["24"] = "Химически продукти",
        ["30"] = "Офис и компютърна техника",
        ["31"] = "Електрооборудване и осветление",
        ["32"] = "Радио, телевизия и телекомуникации",
        ["33"] = "Медицинско оборудване и лекарства",
        ["34"] = "Транспортни средства и оборудване",
        ["35"] = "Охранително, противопожарно и защитно оборудване",
        ["37"] = "Музикални инструменти, спортни стоки и играчки",
        ["38"] = "Лабораторно, оптично и прецизно оборудване",
        ["39"] = "Обзавеждане, домакински уреди и почистващи продукти",
        ["41"] = "Пречистена вода",
        ["42"] = "Промишлени машини",
        ["43"] = "Минна и строителна техника",
        ["44"] = "Строителни материали и конструкции",
        ["45"] = "Строителни работи",
        ["48"] = "Софтуерни пакети и информационни системи",
        ["50"] = "Ремонт и поддръжка",
        ["51"] = "Инсталиране (без софтуер)",
        ["55"] = "Хотели, ресторанти и търговия на дребно",
        ["60"] = "Транспортни услуги",
        ["63"] = "Спомагателни транспортни услуги и туризъм",
        ["64"] = "Пощенски и далекосъобщителни услуги",
        ["65"] = "Комунални услуги",
        ["66"] = "Финансови и застрахователни услуги",
        ["70"] = "Недвижими имоти",
        ["71"] = "Архитектура, инженеринг и надзор",
        ["72"] = "ИТ услуги и разработка на софтуер",
        ["73"] = "Научни изследвания и развойна дейност",
        ["75"] = "Администрация, отбрана и социално осигуряване",
        ["76"] = "Услуги за нефтената и газовата промишленост",
        ["77"] = "Селско и горско стопанство, озеленяване",
        ["79"] = "Бизнес услуги: правни, маркетинг, консултации, охрана",
        ["80"] = "Образование и обучение",
        ["85"] = "Здравеопазване и социални дейности",
        ["90"] = "Отпадъци, почистване и околна среда",
        ["92"] = "Култура, спорт и развлечения",
        ["98"] = "Други обществени и персонални услуги",
    };

    /// <summary>Еднократна поръчка или рамка за бъдещи възлагания (рамково споразумение, ДСП).</summary>
    public static readonly IReadOnlyDictionary<string, string> Arrangements = new Dictionary<string, string>
    {
        [Arrangement.Single] = "Еднократна поръчка",
        [Arrangement.Framework] = "Рамково споразумение",
        [Arrangement.Dps] = "Динамична система за покупки",
    };

    public static string? ContractTypeLabel(string? key) =>
        key is not null && ContractTypes.TryGetValue(key, out var l) ? l : key;

    public static string? DivisionLabel(string? key) =>
        key is not null && CpvDivisions.TryGetValue(key, out var l) ? l : key;

    /// <summary>Липсващата стойност (стари записи) се води еднократна поръчка.</summary>
    public static string ArrangementLabel(string? key) =>
        Arrangements.GetValueOrDefault(key ?? Arrangement.Single, key ?? "");
}

public static class Arrangement
{
    public const string Single = "single";
    public const string Framework = "framework";
    public const string Dps = "dps";

    /// <summary>
    /// Резервен признак по текста (предмет, вид процедура), когато източникът не дава изрично поле:
    /// „Сключване на рамково споразумение за доставка на…“.
    /// </summary>
    public static string? FromText(params string?[] texts)
    {
        foreach (var t in texts)
        {
            if (t is null) continue;
            if (t.Contains("динамична система за покупки", StringComparison.OrdinalIgnoreCase)
                || t.Contains("dynamic purchasing system", StringComparison.OrdinalIgnoreCase)) return Dps;
            if (t.Contains("рамково споразумение", StringComparison.OrdinalIgnoreCase)
                || t.Contains("рамкови споразумения", StringComparison.OrdinalIgnoreCase)
                || t.Contains("framework agreement", StringComparison.OrdinalIgnoreCase)) return Framework;
        }
        return null;
    }
}
