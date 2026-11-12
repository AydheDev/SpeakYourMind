using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpeakYourMind.Core;

public static class ModelCapabilities
{
    // Exact public model IDs and dated snapshots only. Gateways/custom aliases retain manual control.
    // Verified against the official model pages on 2026-11-12; see docs/VALIDATION.md.
    public static string[]? Known(Connection c)
    {
        if (c.Provider != ProviderKind.OpenAI || !Uri.TryCreate(c.BaseUrl, UriKind.Absolute, out var url) || url.Host != "api.openai.com") return null;
        string model = Regex.Replace(c.Model.Trim(), @"-\d{4}-\d{2}-\d{2}$", "");
        return model switch
        {
            "gpt-6.1-sol" => new[] { "low", "medium", "high", "xhigh", "max" },
            "gpt-6.1-terra" or "gpt-6-luna" => new[] { "none", "low", "medium", "high", "xhigh", "max" },
            _ => null
        };
    }
    public static string[] Options(Connection c) => (Known(c) ?? new[] { "low", "medium", "high", "none", "minimal", "xhigh", "max" }).Concat(new[] { "provider default" }).ToArray();
    public static string Error(Connection c)
    {
        var known = Known(c);
        return known != null && c.SendReasoning && c.Reasoning != "provider default" && !known.Contains(c.Reasoning)
            ? "This model does not support the selected reasoning level. Choose " + string.Join(", ", known) + ", or provider default." : "";
    }
}
