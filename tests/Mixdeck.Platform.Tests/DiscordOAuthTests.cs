using System.Net;
using Mixdeck.Platform.Discord;

namespace Mixdeck.Platform.Tests;

public sealed class DiscordOAuthTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesDocumentedFormEncodedGrantAndReturnsTokens(bool refresh)
    {
        string? body = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            request.RequestUri!.AbsoluteUri.Should().Be("https://discord.com/api/oauth2/token");
            request.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
            body = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refreshed\",\"expires_in\":3600}") };
        }));
        var oauth = new DiscordOAuth(http);
        var result = refresh
            ? await oauth.RefreshAsync(new DiscordCredentials { ClientId = "123", ClientSecret = "fake secret", RefreshToken = "fake-refresh" })
            : await oauth.ExchangeAsync("123", "fake secret", "fake-code");
        body.Should().Contain("client_id=123").And.Contain("client_secret=fake+secret");
        body.Should().Contain(refresh ? "grant_type=refresh_token" : "grant_type=authorization_code");
        body.Should().Contain(refresh ? "refresh_token=fake-refresh" : "redirect_uri=http%3A%2F%2Flocalhost");
        result.AccessToken.Should().Be("fake-access");
        result.RefreshToken.Should().Be("fake-refreshed");
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task FailedExchangeDoesNotExposeResponseBodyOrTransportError()
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("fake-sensitive-response") })));
        var exception = await Assert.ThrowsAsync<DiscordAuthorizationException>(() => new DiscordOAuth(http).ExchangeAsync("123", "fake-secret", "fake-code"));
        exception.ToString().Should().NotContain("fake-sensitive-response").And.NotContain("fake-secret");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransientRefreshFailuresAreNotAuthorizationRejections(bool transportFailure)
    {
        using var http = new HttpClient(new Handler(_ => transportFailure
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("fake-sensitive-transport"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("fake-sensitive-response") })));
        var credentials = new DiscordCredentials { ClientId = "123", ClientSecret = "fake-secret", AccessToken = "fake-access", RefreshToken = "fake-refresh" };
        var exception = await Assert.ThrowsAsync<IOException>(() => new DiscordOAuth(http).RefreshAsync(credentials));
        exception.ToString().Should().NotContain("fake-sensitive").And.NotContain("fake-secret");
        credentials.AccessToken.Should().Be("fake-access");
        credentials.RefreshToken.Should().Be("fake-refresh");
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
}
