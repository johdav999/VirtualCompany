using System.Text.Json;

namespace VirtualCompany.Mobile.Services;

public sealed class MobileLocalCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string userScope;
    private readonly Guid companyId;

    public MobileLocalCache(string userScope, Guid companyId)
    {
        this.userScope = Normalize(userScope);
        this.companyId = companyId;
    }

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(value, SerializerOptions);
        await SecureStorage.Default.SetAsync(BuildKey(key), json);
    }

    public async Task<T?> TryReadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await SecureStorage.Default.GetAsync(BuildKey(key));
        return string.IsNullOrWhiteSpace(json)
            ? default
            : JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public async Task QueueAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var queued = await TryReadAsync<List<T>>(key, cancellationToken) ?? [];
        queued.Add(value);
        await SaveAsync(key, queued, cancellationToken);
    }

    public Task<List<T>> ReadQueueAsync<T>(string key, CancellationToken cancellationToken = default) =>
        TryReadAsync<List<T>>(key, cancellationToken).ContinueWith(task => task.Result ?? [], cancellationToken);

    public Task ClearQueueAsync(string key, CancellationToken cancellationToken = default) =>
        SaveAsync(key, Array.Empty<object>(), cancellationToken);

    private string BuildKey(string key) =>
        $"vc.mobile.{userScope}.{companyId:N}.{Normalize(key)}";

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "anonymous"
            : new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
}

public sealed class PendingMobileApprovalDecision
{
    public Guid ClientRequestId { get; set; }
    public Guid ApprovalId { get; set; }
    public Guid? StepId { get; set; }
    public string Decision { get; set; } = string.Empty;
}

public sealed class PendingMobileChatSend
{
    public Guid ClientRequestId { get; set; }
    public Guid ConversationId { get; set; }
    public string Body { get; set; } = string.Empty;
}
