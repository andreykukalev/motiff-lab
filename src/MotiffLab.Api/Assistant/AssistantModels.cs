using System.Text.RegularExpressions;

using Anthropic;
using Anthropic.Foundry;
using Anthropic.Models.Beta.Messages;

namespace MotiffLab.Api.Assistant;

/// <summary>The model the relay uses, or none; the tests put their own in its place.</summary>
public sealed record AssistantModelChoice(IAssistantModel? Model);

/// <summary>
/// Chooses the model from the settings (infra/README.md, "The assistant's model"): Assistant:Provider names the provider,
/// Assistant:Key is its key (a Container App secret, never in the repository), Assistant:Model the model or deployment,
/// Assistant:Endpoint where the provider needs one, Assistant:Effort how hard the model thinks (default medium) and
/// Assistant:TimeoutSeconds how long a request may take. Two providers, both Claude through the Anthropic SDK:
/// <list type="bullet">
/// <item><c>anthropic</c>: the Claude API; the key is a Claude Console API key; the model defaults to <see cref="DefaultModel"/>.</item>
/// <item><c>foundry</c>: Claude on Microsoft Foundry; the key is the Foundry resource's API key; the endpoint is the resource's
/// name or its endpoint (https://&lt;resource&gt;.services.ai.azure.com/anthropic); the model is the deployment's name,
/// <see cref="DefaultModel"/> when it was left as the model's.</item>
/// </list>
/// With no provider or no key, an unknown provider, or Foundry without an endpoint, there is no model: the relay answers "not
/// configured", nothing is sent to any provider and nothing costs anything.
/// </summary>
internal static partial class AssistantModels
{
    /// <summary>The model when Assistant:Model is empty.</summary>
    public const string DefaultModel = "claude-opus-5-5";

    public static AssistantModelChoice Create(IConfiguration config, ILogger logger) => Create(config, logger, null);

    /// <summary>The model; <paramref name="handler"/> takes the provider's HTTP requests in the tests.</summary>
    public static AssistantModelChoice Create(IConfiguration config, ILogger logger, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(config);
        string provider = (config["Assistant:Provider"] ?? "").Trim().ToLowerInvariant();
        string key = (config["Assistant:Key"] ?? "").Trim();
        if (provider.Length == 0 || key.Length == 0)
        {
            return new AssistantModelChoice(null);
        }

        TimeSpan timeout = AssistantOptions.From(config).Timeout;
        HttpClient http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);

        // THE SEAM: one client per provider; the adapter is the same.
        AnthropicClient? client = provider switch
        {
            "anthropic" => new AnthropicClient { ApiKey = key, Timeout = timeout, HttpClient = http },
            "foundry" => FoundryResource(config["Assistant:Endpoint"]) is { } resource
                ? new AnthropicFoundryClient(new AnthropicFoundryApiKeyCredentials(key, resource)) { Timeout = timeout, HttpClient = http }
                : null,
            _ => null,
        };
        if (client is null)
        {
            http.Dispose();
            logger.LogWarning("Assistant:Provider is not anthropic, or foundry with Assistant:Endpoint; the assistant stays not configured.");
            return new AssistantModelChoice(null);
        }

        string model = (config["Assistant:Model"] ?? "").Trim() is { Length: > 0 } named ? named : DefaultModel;
        var settings = new ClaudeSettings($"{provider}/{model}", model, EffortOf(config["Assistant:Effort"], logger), Fallbacks: provider == "anthropic");
        return new AssistantModelChoice(new ClaudeAssistantModel(client, settings, logger));
    }

    /// <summary>The Foundry resource's name, from the name itself or from its endpoint's address; null when it is neither.</summary>
    internal static string? FoundryResource(string? endpoint)
    {
        string value = (endpoint ?? "").Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? address))
        {
            value = address.Host.Split('.')[0];
        }

        return ResourceName().IsMatch(value) ? value : null;
    }

    /// <summary>Assistant:Effort: low, medium (the default), high, xhigh or max; another value is medium, with a warning.</summary>
    internal static Effort EffortOf(string? setting, ILogger logger)
    {
        switch ((setting ?? "").Trim().ToLowerInvariant())
        {
            case "low":
                return Effort.Low;
            case "high":
                return Effort.High;
            case "xhigh":
                return Effort.Xhigh;
            case "max":
                return Effort.Max;
            case "" or "medium":
                return Effort.Medium;
            default:
                logger.LogWarning("Assistant:Effort is not low, medium, high, xhigh or max; medium is used.");
                return Effort.Medium;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceName();
}