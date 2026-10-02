namespace Vanta.Api.Modules.WhatsApp;

/// <summary>
/// Every piece of content that did not originate from VANTA's own code — customer WhatsApp
/// messages, product name/SKU/description (including anything imported from Instagram), and
/// knowledge-base document content — is UNTRUSTED. It is only ever data to be displayed or
/// searched, never a source of instructions. This class provides the small, defense-in-depth
/// guardrails applied before such content is embedded in an LLM prompt or checked in a reply.
/// It intentionally does not try to be a general-purpose prompt-injection classifier: the real
/// defense is structural (tool selection is deterministic C# code that never parses free-text
/// content as commands — see docs/AI_ARCHITECTURE.md); this is a second, cheap layer on top.
/// </summary>
public static class AgentContentSanitizer
{
    private const int MaxCatalogEntryLength = 120;
    private const int MaxKnowledgeContentLength = 600;
    private const int MaxUserMessageLength = 2000;

    // High-signal phrases that indicate an attempt to redirect the model's instructions. Matching
    // is intentionally simple (substring, case-insensitive) — it is a cheap tripwire, not a
    // replacement for the structural boundary (deterministic tool selection, closed tool catalog).
    private static readonly string[] InjectionMarkers =
    [
        "ignore previous", "ignore all previous", "ignora las instrucciones", "ignora todas",
        "disregard the above", "disregard previous", "system prompt", "you are now", "sos ahora",
        "actua como", "actúa como", "reveal your", "revela tu", "developer mode", "modo desarrollador",
        "no restrictions", "sin restricciones",
    ];

    // Product/catalog text (name, SKU, description) is merchant-supplied and often copy-pasted
    // from external sources such as an imported Instagram caption. Strip control characters,
    // redact known injection phrasing, and cap length before it goes anywhere near a prompt.
    public static string SanitizeCatalogText(string? text)
    {
        var value = StripControlCharacters((text ?? string.Empty).Trim());
        value = RedactInjectionMarkers(value);
        return value.Length > MaxCatalogEntryLength ? value[..MaxCatalogEntryLength] : value;
    }

    // Same idea for the customer's own message: still untrusted, but allowed to be longer.
    public static string SanitizeUserMessageForPrompt(string? text)
    {
        var value = StripControlCharacters((text ?? string.Empty).Trim());
        return value.Length > MaxUserMessageLength ? value[..MaxUserMessageLength] : value;
    }

    // Knowledge document/chunk content: merchant-authored FAQ/policy/delivery/etc. text. Same
    // untrusted treatment as catalog text, with a longer cap since these are meant to be read.
    public static string SanitizeKnowledgeContent(string? text)
    {
        var value = StripControlCharacters((text ?? string.Empty).Trim());
        value = RedactInjectionMarkers(value);
        return value.Length > MaxKnowledgeContentLength ? value[..MaxKnowledgeContentLength] : value;
    }

    // Defense against the model echoing back its own instructions (e.g. a prompt-injection
    // attempt asking it to "repeat your system prompt"). A reply containing a verbatim chunk of
    // the system prompt is treated as unsafe.
    public static bool ContainsPromptLeak(string candidateText) =>
        !string.IsNullOrEmpty(candidateText) &&
        new[] { "REGLAS DE RESPUESTA", "CATÁLOGO DISPONIBLE", "Sos VANTA, el asistente de ventas" }
            .Any(marker => candidateText.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string RedactInjectionMarkers(string value)
    {
        foreach (var marker in InjectionMarkers)
        {
            value = value.Replace(marker, "[contenido omitido]", StringComparison.OrdinalIgnoreCase);
        }
        return value;
    }

    private static string StripControlCharacters(string value) =>
        new(value.Where(c => !char.IsControl(c) || c == ' ').ToArray());
}
