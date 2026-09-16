using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FractalExplorerWPF.Models;

namespace FractalExplorerWPF.Infrastructure.Cloud;

public sealed class CloudLoginRequiredException() : Exception("Войдите в FractalCloud. Сеанс отсутствует или завершён.");
public sealed class CloudApiException(HttpStatusCode status) : Exception(status switch
{
    HttpStatusCode.BadRequest => "Сервер отклонил данные. Проверьте название и JSON сохранения.",
    HttpStatusCode.Unauthorized => "Неверный email или пароль.",
    HttpStatusCode.NotFound => "Сохранение отсутствует или недоступно этому пользователю.",
    HttpStatusCode.Conflict => "Сохранение изменилось на другом компьютере.",
    HttpStatusCode.RequestEntityTooLarge => "Сохранение превышает допустимый размер запроса.",
    _ => $"Сервер FractalCloud вернул HTTP {(int)status}. Попробуйте позже."
})
{
    public HttpStatusCode Status { get; } = status;
}

public sealed class FractalCloudClient : IDisposable
{
    private static readonly Lazy<FractalCloudClient> Shared = new(() => new(CloudConnection.Load()));
    public static FractalCloudClient Instance => Shared.Value;
    public const int MaxJsonBytes = 1024 * 1024;
    private readonly HttpClient _http;
    private readonly ICloudCredentialStore _credentials;
    // Serialize authenticated operations, login and logout. A queued request observes rotated tokens;
    // it can never initiate a second concurrent refresh or revive a logged-out session.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _access;
    private string? _refresh;
    private DateTimeOffset _expiresAt;
    public string? Email { get; private set; }
    public string Server => _http.BaseAddress!.GetLeftPart(UriPartial.Authority);

    public FractalCloudClient(CloudConnection connection)
        : this(connection.CreateHttpClient(), new CloudCredentialStore(connection.GetServerUri().GetLeftPart(UriPartial.Authority))) { }

    internal FractalCloudClient(HttpClient http, ICloudCredentialStore credentials)
    {
        _http = http;
        _credentials = credentials;
        CloudCredential? saved = credentials.Read();
        Email = saved?.Email;
        _refresh = saved?.RefreshToken;
    }

    public async Task LoginAsync(string email, string password, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            using var response = await _http.PostAsJsonAsync("/auth/login", new { email, password }, token);
            Check(response);
            CloudTokens tokens = await ReadTokensAsync(response, token);
            Email = email.Trim().ToLowerInvariant();
            try { SetTokens(tokens); }
            catch { ClearTokens(); throw; }
        }
        finally { _gate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try { ClearTokens(); }
        finally { _gate.Release(); }
    }

    private void ClearTokens()
    {
        _access = _refresh = Email = null;
        _expiresAt = default;
        _credentials.Delete();
    }

    private void SetTokens(CloudTokens tokens)
    {
        _credentials.Write(new CloudCredential { Email = Email!, RefreshToken = tokens.RefreshToken });
        _refresh = tokens.RefreshToken;
        _access = tokens.AccessToken;
        // Refresh a little early, but short-lived tokens must still be usable.
        _expiresAt = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn - Math.Min(30, tokens.ExpiresIn / 10.0));
    }

    private static async Task<CloudTokens> ReadTokensAsync(HttpResponseMessage response, CancellationToken token)
    {
        CloudTokens? result = await response.Content.ReadFromJsonAsync<CloudTokens>(token);
        if (result is null || !result.TokenType.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(result.RefreshToken) ||
            result.ExpiresIn is <= 0 or > 31_536_000)
            throw new InvalidOperationException("Сервер вернул некорректный ответ авторизации.");
        return result;
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(_refresh)) throw new CloudLoginRequiredException();
        try
        {
            using var response = await _http.PostAsJsonAsync("/auth/refresh", new { refreshToken = _refresh }, token);
            Check(response);
            SetTokens(await ReadTokensAsync(response, token));
        }
        catch
        {
            ClearTokens();
            if (token.IsCancellationRequested) throw new OperationCanceledException(token);
            throw new CloudLoginRequiredException();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_access is null || DateTimeOffset.UtcNow >= _expiresAt) await RefreshAsync(token);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(method, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _access);
                if (body is not null) request.Content = JsonContent.Create(body);
                HttpResponseMessage response = await _http.SendAsync(request, token);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    if (response.IsSuccessStatusCode) return response;
                    using (response) Check(response);
                }
                response.Dispose();
                if (attempt == 0) await RefreshAsync(token);
            }
            ClearTokens();
            throw new CloudLoginRequiredException();
        }
        finally { _gate.Release(); }
    }

    private static void Check(HttpResponseMessage response)
    {
        // Never expose response bodies or request objects: auth responses contain secrets.
        if (!response.IsSuccessStatusCode) throw new CloudApiException(response.StatusCode);
    }

    public async Task<IReadOnlyList<CloudSave>> ListAsync(CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/saves", null, token);
        return await response.Content.ReadFromJsonAsync<List<CloudSave>>(token)
            ?? throw new InvalidOperationException("Сервер вернул пустой ответ вместо списка сохранений.");
    }

    public async Task<CloudSave> GetAsync(Guid id, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/saves/{id:D}", null, token);
        return await ReadSaveAsync(response, token);
    }

    public async Task<CloudSave> CreateAsync(string name, string jsonData, CancellationToken token)
    {
        ValidateSave(name, jsonData);
        using var response = await SendAsync(HttpMethod.Post, "/api/saves", new { name, jsonData }, token);
        return await ReadSaveAsync(response, token);
    }

    public async Task<CloudSave> UpdateAsync(Guid id, string name, string jsonData, long revision, CancellationToken token)
    {
        ValidateSave(name, jsonData);
        using var response = await SendAsync(HttpMethod.Put, $"/api/saves/{id:D}", new { name, jsonData, revision }, token);
        return await ReadSaveAsync(response, token);
    }

    public async Task DeleteAsync(Guid id, long revision, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/api/saves/{id:D}?revision={revision}", null, token);
    }

    private static async Task<CloudSave> ReadSaveAsync(HttpResponseMessage response, CancellationToken token)
    {
        CloudSave? save = await response.Content.ReadFromJsonAsync<CloudSave>(token);
        if (save is null || save.Id == Guid.Empty || save.Revision < 1)
            throw new InvalidOperationException("Сервер вернул некорректное сохранение.");
        return save;
    }

    public static void ValidateSave(string name, string jsonData)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            throw new InvalidOperationException("Название должно содержать от 1 до 100 символов.");
        if (Encoding.UTF8.GetByteCount(jsonData) > MaxJsonBytes)
            throw new InvalidOperationException("JSON сохранения превышает 1 МиБ. Превью в облако не отправляется.");
        using JsonDocument document = JsonDocument.Parse(jsonData);
    }

    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
