using System.Text.Json;

namespace Mixdeck.Platform.Discord;

/// <summary>Discord's documented authorization-code and refresh-token grants.</summary>
public sealed class DiscordOAuth
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly HttpClient http;
    public DiscordOAuth() : this(SharedClient) { }
    public DiscordOAuth(HttpClient httpClient) => http = httpClient;

    public Task<DiscordCredentials> ExchangeAsync(string clientId, string secret, string code, CancellationToken ct = default) =>
        RequestAsync(clientId, secret, new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = "http://localhost" }, ct);

    public Task<DiscordCredentials> RefreshAsync(DiscordCredentials credentials, CancellationToken ct = default) =>
        RequestAsync(credentials.ClientId, credentials.ClientSecret,
            new() { ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.RefreshToken }, ct);

    // Seul le champ « error » (invalid_client, invalid_grant…) est lu : le reste de la réponse peut contenir des jetons.
    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var code = json.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
            return code is { Length: <= 40 } && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "?";
        }
        catch (Exception) { return "?"; }
    }

    private async Task<DiscordCredentials> RequestAsync(string clientId, string secret, Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = clientId;
        fields["client_secret"] = secret;
        try
        {
            using var content = new FormUrlEncodedContent(fields);
            using var response = await http.PostAsync("https://discord.com/api/oauth2/token", content, ct).ConfigureAwait(false);
            // Never surface Discord response bodies: they can contain credentials.
            if (response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new DiscordAuthorizationException($"OAuth {(int)response.StatusCode} {await ErrorCodeAsync(response, ct).ConfigureAwait(false)}");
            if (!response.IsSuccessStatusCode) throw new IOException("Discord token service is temporarily unavailable.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = json.RootElement;
            var access = root.GetProperty("access_token").GetString();
            var refresh = root.GetProperty("refresh_token").GetString();
            var expires = root.GetProperty("expires_in").GetInt32();
            if (string.IsNullOrWhiteSpace(access) || string.IsNullOrWhiteSpace(refresh) || expires <= 0)
                throw new DiscordAuthorizationException();
            return new DiscordCredentials { ClientId = clientId, ClientSecret = secret, AccessToken = access,
                RefreshToken = refresh, ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expires) };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DiscordAuthorizationException) { throw; }
        catch (Exception) { throw new IOException("Discord token service is temporarily unavailable."); }
    }
}

/// <param name="detail">Code d'erreur renvoyé par Discord (ex. « OAuth 401 invalid_client »), jamais de secret : sert au journal.</param>
public sealed class DiscordAuthorizationException(string? detail = null) : Exception("Discord authorization failed. Check the application and its RPC tester access.")
{
    public string? Detail { get; } = detail;
}
