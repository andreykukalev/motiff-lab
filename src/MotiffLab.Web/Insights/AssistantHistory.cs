using Microsoft.JSInterop;

using MotiffLab.MusicCore.Assistant.Conversation;

namespace MotiffLab.Web.Insights;

/// <summary>A conversation in the list of a piece's conversations: its id, title and when it was last added to.</summary>
public sealed record ConversationEntry(string Id, string Title, long Updated);

/// <summary>
/// The full assistant's conversations kept on this device, by piece (#582 item 17.1, the lead's decision 4), in
/// <c>Scripts/storage/conversation-store.ts</c>: the records music-core writes (<see cref="ConversationRecord"/>), never in
/// the project file and never synced. A store the browser refuses (a private window) keeps nothing and says so by
/// answering nothing; the conversation still works for the session.
/// </summary>
public sealed class AssistantHistory(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    /// <summary>The piece's conversations, newest first; none when the store cannot be read.</summary>
    public async Task<IReadOnlyList<ConversationEntry>> ListAsync(string piece)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<ConversationEntry[]>("listConversations", piece);
        }
        catch (JSException)
        {
            return [];
        }
    }

    /// <summary>The conversation, or null when it is gone or cannot be read.</summary>
    public async Task<ConversationRecord?> LoadAsync(string id)
    {
        try
        {
            return ConversationRecord.Read(await (await ModuleAsync()).InvokeAsync<string?>("loadConversation", id));
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Keeps the conversation about the piece; false when the store refused it.</summary>
    public async Task<bool> SaveAsync(string piece, ConversationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            await (await ModuleAsync()).InvokeVoidAsync("saveConversation", piece, record.Id, record.Title, record.Updated.ToUnixTimeMilliseconds(), record.ToJson());
            return true;
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        IJSObjectReference? module = _module;
        _module = null;
        try
        {
            await (module?.DisposeAsync() ?? ValueTask.CompletedTask);
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and the module with it.
        }
    }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage/conversation-store.js");
}