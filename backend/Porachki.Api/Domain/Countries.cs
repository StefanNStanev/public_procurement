namespace Porachki.Api.Domain;

/// <summary>Държави (ISO3 → ISO2 и име на български). TED връща ISO3.</summary>
public static class Countries
{
    private static readonly (string Iso3, string Iso2, string NameBg)[] All =
    [
        ("AUT", "AT", "Австрия"), ("BEL", "BE", "Белгия"), ("BGR", "BG", "България"),
        ("HRV", "HR", "Хърватия"), ("CYP", "CY", "Кипър"), ("CZE", "CZ", "Чехия"),
        ("DNK", "DK", "Дания"), ("EST", "EE", "Естония"), ("FIN", "FI", "Финландия"),
        ("FRA", "FR", "Франция"), ("DEU", "DE", "Германия"), ("GRC", "GR", "Гърция"),
        ("HUN", "HU", "Унгария"), ("IRL", "IE", "Ирландия"), ("ITA", "IT", "Италия"),
        ("LVA", "LV", "Латвия"), ("LTU", "LT", "Литва"), ("LUX", "LU", "Люксембург"),
        ("MLT", "MT", "Малта"), ("NLD", "NL", "Нидерландия"), ("POL", "PL", "Полша"),
        ("PRT", "PT", "Португалия"), ("ROU", "RO", "Румъния"), ("SVK", "SK", "Словакия"),
        ("SVN", "SI", "Словения"), ("ESP", "ES", "Испания"), ("SWE", "SE", "Швеция"),
        ("NOR", "NO", "Норвегия"), ("ISL", "IS", "Исландия"), ("LIE", "LI", "Лихтенщайн"),
        ("CHE", "CH", "Швейцария"), ("GBR", "GB", "Обединено кралство"),
        ("MKD", "MK", "Северна Македония"), ("SRB", "RS", "Сърбия"), ("MNE", "ME", "Черна гора"),
        ("ALB", "AL", "Албания"), ("BIH", "BA", "Босна и Херцеговина"), ("MDA", "MD", "Молдова"),
        ("UKR", "UA", "Украйна"), ("GEO", "GE", "Грузия"), ("TUR", "TR", "Турция"),
        ("ARM", "AM", "Армения"), ("XKX", "XK", "Косово"),
    ];

    private static readonly Dictionary<string, (string Iso2, string Name)> ByIso3 =
        All.ToDictionary(c => c.Iso3, c => (c.Iso2, c.NameBg), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> NameByIso2 =
        All.ToDictionary(c => c.Iso2, c => c.NameBg, StringComparer.OrdinalIgnoreCase);

    public static string? ToIso2(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        code = code.Trim();
        if (code.Length == 2) return code.ToUpperInvariant();
        return ByIso3.TryGetValue(code, out var c) ? c.Iso2 : code.ToUpperInvariant();
    }

    public static string NameOf(string? iso2) =>
        iso2 is not null && NameByIso2.TryGetValue(iso2, out var n) ? n : iso2 ?? "—";
}
