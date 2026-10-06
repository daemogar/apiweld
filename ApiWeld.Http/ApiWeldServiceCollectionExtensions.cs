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
		// A repeat registration is ignored; see README.md, "Registration".
		if (services.FirstOrDefault(service => service.ServiceType == typeof(Registered<TClient>))?.ImplementationInstance is Registered<TClient> registered)
			return services.AddHttpClient(registered.Name);

		services.AddOptions<ApiClientOptions<TClient>>()
			.Bind(section)
			.Validate(options => options.BaseUrl is { IsAbsoluteUri: true }, "BaseUrl is required and must be an absolute URL.")
			.Validate(options => options.BaseUrl is not { IsAbsoluteUri: true } url || (url.Query.Length == 0 && url.Fragment.Length == 0),
				"BaseUrl must not carry a query or fragment.")
			.Validate(options => options.TokenExchange is null
				|| (!string.IsNullOrWhiteSpace(options.TokenExchange.Endpoint) && !string.IsNullOrWhiteSpace(options.TokenExchange.ApiKey)),
				"TokenExchange needs both Endpoint and ApiKey.")
			.ValidateOnStart();

		services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<ApiClientOptions<TClient>>>().Value);

		var exchangeName = TokenExchangeClientName<TClient>();

		services.AddHttpClient(exchangeName, (provider, http) => Configure(provider.GetRequiredService<ApiClientOptions<TClient>>(), http))
			.ConfigurePrimaryHttpMessageHandler(PrimaryHandler<TClient>)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan);
		services.TryAddKeyedSingleton(exchangeName, (provider, _) => new TokenSource(
			provider.GetRequiredService<ApiClientOptions<TClient>>().TokenExchange!,
			provider.GetRequiredService<IHttpClientFactory>().CreateClient(exchangeName),
			provider.GetService<TimeProvider>()));

		// Chosen when the pipeline is built, so an exchange configured in code counts as much as one in the section.
		var builder = services.AddHttpClient<TClient>((provider, http) => Configure(provider.GetRequiredService<ApiClientOptions<TClient>>(), http))
			.ConfigurePrimaryHttpMessageHandler(PrimaryHandler<TClient>)
			.SetHandlerLifetime(Timeout.InfiniteTimeSpan)
			.AddHttpMessageHandler(provider => provider.GetRequiredService<ApiClientOptions<TClient>>().TokenExchange is null
				? new PassThroughHandler()
				: new TokenExchangeHandler(provider.GetRequiredKeyedService<TokenSource>(exchangeName)));

		services.AddSingleton(new Registered<TClient>(builder.Name));

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

	/// <summary>Records that a client type is registered, and under which client name.</summary>
	sealed record Registered<TClient>(string Name);
}
