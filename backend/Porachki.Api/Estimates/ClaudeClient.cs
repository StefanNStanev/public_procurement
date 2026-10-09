using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Porachki.Api.Infrastructure;

namespace Porachki.Api.Estimates;

/// <summary>Минимален клиент за Claude Messages API.</summary>
public class ClaudeClient(HttpClient http, IOptions<EstimateOptions> options, ILogger<ClaudeClient> log)
{
    private readonly EstimateOptions _opt = options.Value;

    public record Result(string Text, int InputTokens, int OutputTokens, string? StopReason);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_opt.AnthropicApiKey);

    /// <param name="content">Съдържание на потребителското съобщение (текстови и document блокове).</param>
    /// <param name="fallbackInstruction">Текст, добавян при повторен опит без structured outputs.</param>
    public async Task<Result> CompleteAsync(string system, JsonArray content, JsonObject? schema, string? fallbackInstruction, CancellationToken ct)
    {
        var useSchema = schema is not null;
        for (var attempt = 1; ; attempt++)
        {
            var body = new JsonObject
            {
                ["model"] = _opt.Model,
                ["max_tokens"] = _opt.MaxOutputTokens,
                ["system"] = system,
                ["messages"] = new JsonArray(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = content.DeepClone(),
                }),
            };
            if (useSchema)
                body["output_config"] = new JsonObject { ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = schema!.DeepClone() } };
            else if (schema is not null && fallbackInstruction is not null)
                ((JsonArray)body["messages"]![0]!["content"]!).Add(new JsonObject { ["type"] = "text", ["text"] = fallbackInstruction });

            using var req = new HttpRequestMessage(HttpMethod.Post, _opt.AnthropicUrl) { Content = JsonContent.Create(body) };
            req.Headers.Add("x-api-key", _opt.AnthropicApiKey);
            req.Headers.Add("anthropic-version", "2023-06-01");

            using var resp = await http.SendAsync(req, ct);
            var raw = await resp.Content.ReadAsStringAsync(ct);
            var code = (int)resp.StatusCode;

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var text = string.Concat(root.Prop("content").Items()
                    .Where(b => b.Prop("type").Str() == "text")
                    .Select(b => b.Prop("text").Str() ?? ""));
                return new Result(text,
                    (int)(root.Path("usage", "input_tokens").Dec() ?? 0),
                    (int)(root.Path("usage", "output_tokens").Dec() ?? 0),
                    root.Prop("stop_reason").Str());
            }

            // Схемата не е приета (неподдържан модел/конструкция) → без structured outputs
            if (code == 400 && useSchema && (raw.Contains("output_config") || raw.Contains("schema")))
            {
                log.LogWarning("Claude не прие JSON схемата, повтарям без structured outputs: {Body}", Short(raw));
                useSchema = false;
                continue;
            }
            if (attempt < 4 && (code is 429 or 529 || code >= 500))
            {
                var wait = TimeSpan.FromSeconds(10 * attempt);
                log.LogWarning("Claude HTTP {Code}, нов опит след {Wait}s", code, wait.TotalSeconds);
                await Task.Delay(wait, ct);
                continue;
            }
            throw new HttpRequestException(code switch
            {
                401 => "Невалиден Anthropic API ключ (Estimates:AnthropicApiKey).",
                413 => "Документацията е твърде голяма за една заявка. Намалете Estimates:MaxTextChars.",
                _ => $"Claude API върна HTTP {code}: {Short(raw)}",
            });
        }
    }

    private static string Short(string s) => s.Length > 500 ? s[..500] + "…" : s;
}
