using MostaqlK.Core.Sources;

namespace MostaqlK.Infrastructure.Http;

/// <summary>
/// Builds a source's <see cref="HttpClient"/> from the values the descriptor supplies, so the
/// per-site request headers (the bot-filter <c>User-Agent</c>, <c>Accept</c>, <c>Accept-Language</c>)
/// live in the source module and never as inline literals in the DI wiring. The mostaql client is
/// seeded with the exact headers the app sends today, so bot-filter behavior is unchanged.
/// </summary>
public interface IHttpClientPolicyFactory
{
    /// <summary>An <see cref="HttpClient"/> carrying <paramref name="descriptor"/>'s default request headers.</summary>
    public HttpClient Create(ISourceDescriptor descriptor);
}

/// <inheritdoc cref="IHttpClientPolicyFactory"/>
public sealed class HttpClientPolicyFactory : IHttpClientPolicyFactory
{
    public HttpClient Create(ISourceDescriptor descriptor)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(descriptor.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd(descriptor.AcceptHeader);
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd(descriptor.AcceptLanguageHeader);
        return http;
    }
}
