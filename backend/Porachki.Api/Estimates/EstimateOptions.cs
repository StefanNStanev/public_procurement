namespace Porachki.Api.Estimates;

public class EstimateOptions
{
    /// <summary>Задава се през user-secrets или променлива на средата Estimates__AnthropicApiKey.</summary>
    public string AnthropicApiKey { get; set; } = "";
    public string AnthropicUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public int MaxOutputTokens { get; set; } = 16000;
    /// <summary>Цена за 1M входни/изходни токена (USD) – само за показване на разхода.</summary>
    public decimal PriceInputPerMTok { get; set; } = 2m;
    public decimal PriceOutputPerMTok { get; set; } = 10m;

    /// <summary>Максимален брой символи текст от документите, подаван на модела.</summary>
    public int MaxTextChars { get; set; } = 600_000;
    public int MaxFiles { get; set; } = 60;
    public int MaxFileMb { get; set; } = 40;
    public int MaxTotalMb { get; set; } = 250;
    /// <summary>Сканирани PDF-и (без текстов слой) се подават като документ, за да ги „прочете“ моделът.</summary>
    public int MaxNativePdfs { get; set; } = 3;
    public int MaxNativePdfMb { get; set; } = 15;
    /// <summary>Предпазител срещу неволни разходи.</summary>
    public int MaxEstimatesPerDay { get; set; } = 30;

    public string EopServiceUrl { get; set; } = "https://service.eop.bg/NX1Service.svc";
    public string EopAppUrl { get; set; } = "https://app.eop.bg";
}
