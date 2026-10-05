using ApiWeld.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a generated client, its options and its HTTP pipeline.</summary>
/// <remarks>See README.md, "Registration".</remarks>
public static class ApiWeldServiceCollectionExtensions
{
	/// <summary>The name of the bare client the token exchange for <typeparamref name="TClient"/> uses.</summary>
	public static string TokenExchangeClientName<TClient>() => typeof(TClient).FullName + ".TokenExchange";

	/// <summary>Registers <typeparamref name="TClient"/> with options bound from <paramref name="section"/>; chain further handlers on the result.</summary>
	public static IHttpClientBuilder AddApiWeldClient<TClient>(this IServiceCollection services, IConfiguration section)
		where TClient : ApiClient
	{
		services.AddOptions<ApiClientOptions<TClient>>()
			.Bind(section)
			.Validate(options => options.BaseUrl is { IsAbsoluteUri: true }, "BaseUrl is required and must be an absolute URL.")
			.Validate(options => options.TokenExchange is null
				|| (!string.IsNullOrWhiteSpace(options.TokenExchange.Endpoint) && !string.IsNullOrWhiteSpace(options.TokenExchange.ApiKey)),
				"TokenExchange needs both Endpoint and ApiKey.")
			.ValidateOnStart();

		services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<ApiClientOptions<TClient>>>().Value);

		var exchanges = section.GetSection(nameof(ApiClientOptions.TokenExchange)).Exists();
		var exchangeName = TokenExchangeClientName<TClient>();

		if (exchanges)
		{
			services.AddHttpClient(exchangeName, (provider, http) => Configure(provider.GetRequiredService<ApiClientOptions<TClient>>(), http))
				.ConfigurePrimaryHttpMessageHandler(PrimaryHandler<TClient>)
				.SetHandlerLifetime(Timeout.InfiniteTimeSpan);
			services.TryAddKeyedSingleton(exchangeName, (provider, _) => new TokenSource(
				provider.GetRequiredService<ApiClientOptions<TClient>>().TokenExchange!,
				provider.GetRequiredService<IHttpClientFactory>().CreateClient(exchangeName),
				provider.GetService<TimeProvider>()));
		}

		var builder = services.AddHttpClient<TClient>((provider, http) => Configure(provider.GetRequiredService<ApiClientOptions<TClient>>(), http))
			.ConfigurePrimaryHttpMessageHandler(PrimaryHandler<TClient>)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

		if (exchanges)
			builder.AddHttpMessageHandler(provider => new TokenExchangeHandler(provider.GetRequiredKeyedService<TokenSource>(exchangeName)));

		return builder;
	}

	static HttpMessageHandler PrimaryHandler<TClient>(IServiceProvider provider) where TClient : ApiClient => new SocketsHttpHandler
	{
		PooledConnectionLifetime = provider.GetRequiredService<ApiClientOptions<TClient>>().PooledConnectionLifetime
	};

	static void Configure(ApiClientOptions options, HttpClient http)
	{
		var baseUrl = options.BaseUrl!;
		http.BaseAddress = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
		http.Timeout = options.Timeout;
	}
}
