namespace MotiffLab.Api.Assistant;

/// <summary>The model the relay uses, or none; the tests put their own in its place.</summary>
public sealed record AssistantModelChoice(IAssistantModel? Model);

/// <summary>
/// Chooses the model from the settings Assistant:Provider, Assistant:Model, Assistant:Endpoint and Assistant:Key (a
/// Container App secret, never in the repository). <b>No provider has an adapter yet</b>: until Andrei chooses one
/// (decision 4 in governance/README.md), every setting gives no model and the relay answers "not configured", so nothing
/// is sent to any provider and nothing costs anything.
/// </summary>
internal static class AssistantModels
{
    public static AssistantModelChoice Create(IConfiguration config, ILogger logger)
    {
        string provider = config["Assistant:Provider"] ?? "";
        string key = config["Assistant:Key"] ?? "";
        if (provider.Length == 0 || key.Length == 0)
        {
            return new AssistantModelChoice(null);
        }

        // THE SEAM: a provider's adapter (one class implementing IAssistantModel, built on HttpClient or the provider's
        // SDK) is added here, for example:
        //     "<provider>" => new <Provider>AssistantModel(config["Assistant:Model"], config["Assistant:Endpoint"], key),
        IAssistantModel? model = provider switch
        {
            _ => null,
        };
        if (model is null)
        {
            logger.LogWarning("Assistant:Provider names a provider with no adapter; the assistant stays not configured.");
        }

        return new AssistantModelChoice(model);
    }
}