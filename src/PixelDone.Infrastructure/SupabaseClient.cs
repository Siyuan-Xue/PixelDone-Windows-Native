using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PixelDone.Infrastructure;

public sealed class SupabaseClient
{
    public const string ExpectedSchema = "3.2";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _httpClient;
    public SupabaseConfig Config { get; }

    public SupabaseClient(SupabaseConfig config, HttpClient? httpClient = null)
    {
        Config = config;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public static SupabaseClient FromEnvironment() =>
        new(SupabaseConfig.FromEnvironment());

    public Task<AuthSession> SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        TokenRequestAsync(
            "/auth/v1/token?grant_type=password",
            new { email = email.Trim(), password },
            null,
            cancellationToken);

    public Task<AuthSession> SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default) =>
        TokenRequestAsync(
            "/auth/v1/signup",
            new { email = email.Trim(), password },
            null,
            cancellationToken);

    public async Task SignOutAsync(
        AuthSession session,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await RequestAsync<JsonElement>(
                HttpMethod.Post,
                "/auth/v1/logout",
                session.AccessToken,
                null,
                cancellationToken);
        }
        catch
        {
            // Local sign-out must remain possible when the server is unavailable.
        }
    }

    public async Task ChangePasswordAsync(
        AuthSession session,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(session.Email))
        {
            throw new InvalidOperationException("The signed-in account has no email address.");
        }

        var verified = await SignInAsync(
            session.Email,
            currentPassword,
            cancellationToken);
        if (verified.UserId != session.UserId)
        {
            throw new InvalidOperationException("Password verification returned another account.");
        }

        _ = await RequestAsync<JsonElement>(
            HttpMethod.Put,
            "/auth/v1/user",
            verified.AccessToken,
            new { password = newPassword },
            cancellationToken);
    }

    public Task<AuthSession> RefreshIfNeededAsync(
        AuthSession session,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return !force && session.ExpiresAtMillis - 60_000 > now
            ? Task.FromResult(session)
            : TokenRequestAsync(
                "/auth/v1/token?grant_type=refresh_token",
                new { refresh_token = session.RefreshToken },
                session,
                cancellationToken);
    }

    public Task<T> RpcAsync<T>(
        AuthSession session,
        string function,
        object body,
        CancellationToken cancellationToken = default) =>
        RequestAsync<T>(
            HttpMethod.Post,
            $"/rest/v1/rpc/{function}",
            session.AccessToken,
            body,
            cancellationToken);

    public async Task UploadTodoImageAsync(
        AuthSession session,
        string objectPath,
        string contentType,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        RequireSafeObjectPath(objectPath);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"/storage/v1/object/pixeldone-todo-images/{objectPath}",
            session.AccessToken);
        request.Headers.Add("x-upsert", "true");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
    }

    public async Task<byte[]> DownloadTodoImageAsync(
        AuthSession session,
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        RequireSafeObjectPath(objectPath);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"/storage/v1/object/authenticated/pixeldone-todo-images/{objectPath}",
            session.AccessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await RequireSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task DeleteTodoImageAsync(
        AuthSession session,
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        RequireSafeObjectPath(objectPath);
        using var request = CreateRequest(
            HttpMethod.Delete,
            "/storage/v1/object/pixeldone-todo-images",
            session.AccessToken);
        request.Content = JsonContent.Create(
            new { prefixes = new[] { objectPath } },
            options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.NotFound)
        {
            await RequireSuccessAsync(response, cancellationToken);
        }
    }

    public Uri RealtimeUri()
    {
        var builder = new UriBuilder(Config.BaseUrl)
        {
            Scheme = Config.BaseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase)
                ? "wss"
                : "ws",
            Path = "/realtime/v1/websocket",
            Query = $"apikey={Uri.EscapeDataString(Config.PublishableKey)}&vsn=1.0.0",
        };
        return builder.Uri;
    }

    public async Task ListenForInvalidationsAsync(
        AuthSession session,
        Func<Task> invalidated,
        CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(RealtimeUri(), cancellationToken);
        var topic = $"realtime:pixeldone-{session.UserId}";
        var joins = new[]
        {
            "todo_checklists",
            "todo_items",
            "todo_attachments",
            "user_settings",
            "sync_tombstones",
        }.Select(
            table => new
            {
                @event = "*",
                schema = "public",
                table,
                filter = $"owner_user_id=eq.{session.UserId}",
            });
        await SendWebSocketJsonAsync(
            socket,
            new
            {
                topic,
                @event = "phx_join",
                payload = new
                {
                    config = new
                    {
                        broadcast = new { self = false },
                        presence = new { key = "" },
                        postgres_changes = joins,
                        @private = false,
                    },
                    access_token = session.AccessToken,
                },
                @ref = "1",
            },
            cancellationToken);

        var buffer = new byte[64 * 1024];
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            var json = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            var root = json.RootElement;
            if (root.TryGetProperty("event", out var eventName) &&
                eventName.GetString() is "postgres_changes")
            {
                await invalidated();
            }
        }
    }

    public static void RequireSchema(string value)
    {
        if (value != ExpectedSchema)
        {
            throw new InvalidOperationException(
                $"PixelDone requires cloud schema 3.2; server returned {value}.");
        }
    }

    private async Task<AuthSession> TokenRequestAsync(
        string path,
        object body,
        AuthSession? previous,
        CancellationToken cancellationToken)
    {
        var response = await RequestAsync<TokenResponse>(
            HttpMethod.Post,
            path,
            null,
            body,
            cancellationToken);
        var refreshToken = response.RefreshToken ?? previous?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new InvalidOperationException("Supabase did not return a refresh token.");
        }

        return new AuthSession(
            response.User.Id,
            response.User.Email ?? previous?.Email,
            response.AccessToken,
            refreshToken,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
            (response.ExpiresIn ?? 3600) * 1000);
    }

    private async Task<T> RequestAsync<T>(
        HttpMethod method,
        string path,
        string? bearer,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, bearer);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                RemoteMessage(text) ?? $"Supabase HTTP {(int)response.StatusCode}: {text}",
                null,
                response.StatusCode);
        }

        return JsonSerializer.Deserialize<T>(
                   string.IsNullOrWhiteSpace(text) ? "null" : text,
                   JsonOptions) ??
               throw new InvalidOperationException("Supabase returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(
        HttpMethod method,
        string path,
        string? bearer)
    {
        var request = new HttpRequestMessage(method, $"{Config.BaseUrl}{path}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("apikey", Config.PublishableKey);
        if (!string.IsNullOrWhiteSpace(bearer))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return request;
    }

    private static async Task RequireSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            RemoteMessage(text) ?? $"Supabase Storage HTTP {(int)response.StatusCode}: {text}",
            null,
            response.StatusCode);
    }

    private static string? RemoteMessage(string text)
    {
        try
        {
            var json = JsonDocument.Parse(text).RootElement;
            foreach (var name in new[] { "msg", "message", "error_description" })
            {
                if (json.TryGetProperty(name, out var value))
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static void RequireSafeObjectPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '/')))
        {
            throw new InvalidOperationException("Unsafe Supabase Storage object path.");
        }
    }

    private static async Task SendWebSocketJsonAsync(
        ClientWebSocket socket,
        object payload,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        await socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private sealed record TokenResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
        string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        string? RefreshToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        long? ExpiresIn,
        TokenUser User);

    private sealed record TokenUser(string Id, string? Email);
}
