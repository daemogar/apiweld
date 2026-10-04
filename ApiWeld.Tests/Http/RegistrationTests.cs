using System.Net;

using ApiWeld.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ApiWeld.Tests.Http;

public class RegistrationTests
{
	public sealed class SampleClient(HttpClient http, ApiClientOptions<SampleClient> options) : ApiClient(http, options)
	{
		public HttpClient Http { get; } = http;
		public ApiClientOptions Options { get; } = options;
	}

	static IConfigurationSection Section(Dictionary<string, string?> values)
		=> new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("ExampleApi");

	[Fact]
	public async Task Binds_options_and_configures_the_http_client()
	{
		var data = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
		var services = new ServiceCollection();
		services.AddApiWeldClient<SampleClient>(Section(new()
		{
			["ExampleApi:BaseUrl"] = "https://api.example.test/root",
			["ExampleApi:Timeout"] = "00:00:05",
			["ExampleApi:VersionMismatch"] = "Warn"
		})).ConfigurePrimaryHttpMessageHandler(() => data);

		var client = services.BuildServiceProvider().GetRequiredService<SampleClient>();
		await client.Http.GetAsync("a", TestContext.Current.CancellationToken);

		Assert.Equal("https://api.example.test/root/a", data.Requests.Single().Request.RequestUri!.ToString());

		Assert.Equal("https://api.example.test/root/", client.Http.BaseAddress!.ToString());
		Assert.Equal(TimeSpan.FromSeconds(5), client.Http.Timeout);
		Assert.Equal(VersionMismatchBehavior.Warn, client.Options.VersionMismatch);
		Assert.Null(client.Options.TokenExchange);
	}

	[Fact]
	public void Refuses_options_without_a_base_url()
	{
		var services = new ServiceCollection();
		services.AddApiWeldClient<SampleClient>(Section(new() { ["ExampleApi:Timeout"] = "00:00:05" }));

		var exception = Assert.Throws<OptionsValidationException>(() => services.BuildServiceProvider().GetRequiredService<SampleClient>());

		Assert.Contains("BaseUrl", exception.Message);
	}

	[Fact]
	public void Refuses_a_token_exchange_without_a_key()
	{
		var services = new ServiceCollection();
		services.AddApiWeldClient<SampleClient>(Section(new()
		{
			["ExampleApi:BaseUrl"] = "https://api.example.test/",
			["ExampleApi:TokenExchange:Endpoint"] = "/auth"
		}));

		var exception = Assert.Throws<OptionsValidationException>(() => services.BuildServiceProvider().GetRequiredService<SampleClient>());

		Assert.Contains("ApiKey", exception.Message);
	}

	[Fact]
	public async Task Posts_the_token_exchange_beneath_the_base_url()
	{
		var data = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
		var exchange = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("tok") });

		var services = new ServiceCollection();
		services.AddApiWeldClient<SampleClient>(Section(new()
		{
			["ExampleApi:BaseUrl"] = "https://api.example.test/root",
			["ExampleApi:TokenExchange:Endpoint"] = "/auth",
			["ExampleApi:TokenExchange:ApiKey"] = "key"
		})).ConfigurePrimaryHttpMessageHandler(() => data);
		services.AddHttpClient(ApiWeldServiceCollectionExtensions.TokenExchangeClientName<SampleClient>())
			.ConfigurePrimaryHttpMessageHandler(() => exchange);

		await services.BuildServiceProvider().GetRequiredService<SampleClient>().Http.GetAsync("a", TestContext.Current.CancellationToken);

		Assert.Equal("https://api.example.test/root/auth", exchange.Requests.Single().Request.RequestUri!.ToString());
	}

	[Fact]
	public async Task Adds_the_token_handler_only_when_a_token_exchange_is_configured()
	{
		var data = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
		var exchange = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("tok") });

		var withExchange = new ServiceCollection();
		withExchange.AddApiWeldClient<SampleClient>(Section(new()
		{
			["ExampleApi:BaseUrl"] = "https://api.example.test/",
			["ExampleApi:TokenExchange:Endpoint"] = "/auth",
			["ExampleApi:TokenExchange:ApiKey"] = "key"
		})).ConfigurePrimaryHttpMessageHandler(() => data);
		withExchange.AddHttpClient(ApiWeldServiceCollectionExtensions.TokenExchangeClientName<SampleClient>())
			.ConfigurePrimaryHttpMessageHandler(() => exchange);

		await withExchange.BuildServiceProvider().GetRequiredService<SampleClient>().Http.GetAsync("a", TestContext.Current.CancellationToken);

		Assert.Equal("Bearer tok", data.Requests.Single().Request.Headers.Authorization?.ToString());

		var plainData = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
		var without = new ServiceCollection();
		without.AddApiWeldClient<SampleClient>(Section(new() { ["ExampleApi:BaseUrl"] = "https://api.example.test/" }))
			.ConfigurePrimaryHttpMessageHandler(() => plainData);

		await without.BuildServiceProvider().GetRequiredService<SampleClient>().Http.GetAsync("a", TestContext.Current.CancellationToken);

		Assert.Null(plainData.Requests.Single().Request.Headers.Authorization);
	}
}
