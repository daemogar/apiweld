using System.Net;
using System.Text;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class TokenExchangeTests
{
	sealed class ManualClock(DateTimeOffset start) : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = start;

		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>Content that can be serialized once, like a request body streamed to a socket.</summary>
	sealed class OneShotContent(string text) : HttpContent
	{
		bool used;

		protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
		{
			if (used)
				throw new InvalidOperationException("The content was already sent once.");

			used = true;
			await stream.WriteAsync(Encoding.UTF8.GetBytes(text));
		}

		protected override bool TryComputeLength(out long length)
		{
			length = 0;

			return false;
		}
	}

	/// <summary>A server whose answer is awaited, so a test can hold requests in flight.</summary>
	sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
	}

	static readonly DateTimeOffset Start = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

	static string Base64Url(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	static string Jwt(DateTimeOffset expires) => $"{Base64Url("""{"alg":"none"}""")}.{Base64Url($$"""{"exp":{{expires.ToUnixTimeSeconds()}}}""")}.signature";

	static TokenExchangeOptions Options(TokenFormat format = TokenFormat.RawText, string? property = null) => new()
	{
		Endpoint = "/auth",
		ApiKey = "secret-key",
		TokenFormat = format,
		TokenProperty = property
	};

	/// <summary>An exchange server handing out the given tokens in turn, and a data server answering with the given statuses in turn.</summary>
	static (HttpClient Client, FakeHandler Exchange, FakeHandler Data, ManualClock Clock) Setup(
		string[] tokens, HttpStatusCode[]? statuses = null, TokenExchangeOptions? options = null)
	{
		var issued = 0;
		var exchange = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(tokens[Math.Min(issued++, tokens.Length - 1)])
		});

		var answered = 0;
		var data = new FakeHandler((_, _) => new HttpResponseMessage(statuses is null ? HttpStatusCode.OK : statuses[Math.Min(answered++, statuses.Length - 1)]));

		var clock = new ManualClock(Start);
		var source = new TokenSource(options ?? Options(), exchange.Client(), clock);
		var client = new HttpClient(new TokenExchangeHandler(source) { InnerHandler = data }) { BaseAddress = new Uri("https://api.example.test/") };

		return (client, exchange, data, clock);
	}

	static string? Bearer(FakeHandler data, int index) => data.Requests[index].Request.Headers.Authorization?.ToString();

	[Fact]
	public async Task Exchanges_the_key_and_sends_the_token_as_a_bearer()
	{
		var (client, exchange, data, _) = Setup(["token-1"]);

		await client.GetAsync("api/widgets", TestContext.Current.CancellationToken);

		var sent = exchange.Requests.Single().Request;
		Assert.Equal(HttpMethod.Post, sent.Method);
		Assert.Equal("https://api.example.test/auth", sent.RequestUri!.ToString());
		Assert.Equal("Bearer secret-key", sent.Headers.Authorization!.ToString());
		Assert.Equal("Bearer token-1", Bearer(data, 0));
	}

	[Fact]
	public async Task Reuses_a_token_until_its_refresh_point()
	{
		var (client, exchange, data, clock) = Setup([Jwt(Start.AddMinutes(10)), "token-2"]);

		await client.GetAsync("a", TestContext.Current.CancellationToken);
		clock.Now = Start.AddMinutes(9).AddSeconds(29);
		await client.GetAsync("b", TestContext.Current.CancellationToken);

		Assert.Single(exchange.Requests);

		clock.Now = Start.AddMinutes(9).AddSeconds(31);
		await client.GetAsync("c", TestContext.Current.CancellationToken);

		Assert.Equal(2, exchange.Requests.Count);
		Assert.Equal("Bearer token-2", Bearer(data, 2));
	}

	[Fact]
	public async Task Keeps_a_token_without_an_expiry_for_the_fallback_lifetime()
	{
		var (client, exchange, _, clock) = Setup(["opaque-1", "opaque-2"]);

		await client.GetAsync("a", TestContext.Current.CancellationToken);
		clock.Now = Start.AddMinutes(3).AddSeconds(59);
		await client.GetAsync("b", TestContext.Current.CancellationToken);

		Assert.Single(exchange.Requests);

		clock.Now = Start.AddMinutes(4).AddSeconds(1);
		await client.GetAsync("c", TestContext.Current.CancellationToken);

		Assert.Equal(2, exchange.Requests.Count);
	}

	[Fact]
	public async Task Invalidates_and_retries_once_on_401_replaying_the_body()
	{
		var (client, exchange, data, _) = Setup(["token-1", "token-2"], [HttpStatusCode.Unauthorized, HttpStatusCode.OK]);

		var response = await client.PostAsync("api/widgets", new OneShotContent("{\"name\":\"w\"}"), TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal(2, exchange.Requests.Count);
		Assert.Equal(new[] { "{\"name\":\"w\"}", "{\"name\":\"w\"}" }, data.Requests.Select(r => r.Body));
		Assert.Equal("Bearer token-2", Bearer(data, 1));
	}

	[Fact]
	public async Task Retries_only_once()
	{
		var (client, _, data, _) = Setup(["token-1", "token-2", "token-3"], [HttpStatusCode.Unauthorized]);

		var response = await client.GetAsync("api/widgets", TestContext.Current.CancellationToken);

		Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
		Assert.Equal(2, data.Requests.Count);
	}

	[Fact]
	public async Task Reads_the_token_from_a_json_property_when_configured()
	{
		var (client, _, data, _) = Setup(["""{ "access_token": "from-json" }"""], options: Options(TokenFormat.JsonProperty, "access_token"));

		await client.GetAsync("a", TestContext.Current.CancellationToken);

		Assert.Equal("Bearer from-json", Bearer(data, 0));
	}

	[Fact]
	public async Task Exchanges_once_for_concurrent_requests()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var exchanges = 0;
		var exchange = new AsyncHandler(async _ =>
		{
			Interlocked.Increment(ref exchanges);
			await release.Task;

			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("token-1") };
		});
		var source = new TokenSource(Options(), new HttpClient(exchange) { BaseAddress = new Uri("https://api.example.test/") });

		var callers = Enumerable.Range(0, 8).Select(_ => source.GetAsync(TestContext.Current.CancellationToken)).ToList();
		release.SetResult();

		Assert.All(await Task.WhenAll(callers), token => Assert.Equal("token-1", token));
		Assert.Equal(1, exchanges);
	}

	[Fact]
	public async Task Exchanges_once_when_concurrent_requests_are_refused_together()
	{
		var issued = 0;
		var exchange = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent($"token-{Interlocked.Increment(ref issued)}")
		});
		var bothSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var stale = 0;
		var data = new AsyncHandler(async request =>
		{
			if (request.Headers.Authorization?.Parameter != "token-1")
				return new HttpResponseMessage(HttpStatusCode.OK);

			if (Interlocked.Increment(ref stale) == 2)
				bothSent.SetResult();

			await bothSent.Task;

			return new HttpResponseMessage(HttpStatusCode.Unauthorized);
		});
		var source = new TokenSource(Options(), exchange.Client());
		var client = new HttpClient(new TokenExchangeHandler(source) { InnerHandler = data }) { BaseAddress = new Uri("https://api.example.test/") };

		var responses = await Task.WhenAll(
			client.GetAsync("a", TestContext.Current.CancellationToken),
			client.GetAsync("b", TestContext.Current.CancellationToken));

		Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
		Assert.Equal(2, exchange.Requests.Count);
	}

	[Fact]
	public async Task Disposes_the_retried_request_once_it_is_answered()
	{
		var (client, _, data, _) = Setup(["token-1", "token-2"], [HttpStatusCode.Unauthorized, HttpStatusCode.OK]);

		using var response = await client.PostAsync("api/widgets", new StringContent("{}"), TestContext.Current.CancellationToken);

		Assert.Throws<ObjectDisposedException>(() => data.Requests[1].Request.Content!.ReadAsStream(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Reports_an_exchange_that_cannot_reach_the_endpoint()
	{
		var exchange = new FakeHandler((_, _) => throw new HttpRequestException("No connection could be made."));
		var source = new TokenSource(Options(), exchange.Client());

		var exception = await Assert.ThrowsAsync<TokenExchangeException>(() => source.GetAsync(TestContext.Current.CancellationToken));

		Assert.Equal("Token exchange at /auth could not be completed: No connection could be made.", exception.Message);
		Assert.IsType<HttpRequestException>(exception.InnerException);
		Assert.DoesNotContain("secret-key", exception.Message);
	}

	[Fact]
	public async Task Reports_an_exchange_that_times_out()
	{
		var exchange = new FakeHandler((_, _) => throw new TaskCanceledException("The request timed out.", new TimeoutException()));
		var source = new TokenSource(Options(), exchange.Client());

		var exception = await Assert.ThrowsAsync<TokenExchangeException>(() => source.GetAsync(TestContext.Current.CancellationToken));

		Assert.Equal("Token exchange at /auth could not be completed: The request timed out.", exception.Message);
		Assert.IsType<TaskCanceledException>(exception.InnerException);
	}

	[Fact]
	public async Task Lets_the_callers_own_cancellation_through_unwrapped()
	{
		using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		var exchange = new FakeHandler((_, _) =>
		{
			cancel.Cancel();
			throw new OperationCanceledException(cancel.Token);
		});
		var source = new TokenSource(Options(), exchange.Client());

		var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetAsync(cancel.Token));

		Assert.IsNotType<TokenExchangeException>(exception);
	}

	[Fact]
	public async Task Names_the_endpoint_but_never_the_key_when_the_exchange_fails()
	{
		var exchange = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
		var source = new TokenSource(Options(), exchange.Client());

		var exception = await Assert.ThrowsAsync<TokenExchangeException>(() => source.GetAsync(TestContext.Current.CancellationToken));

		Assert.Equal("Token exchange at /auth failed with 403 Forbidden.", exception.Message);
		Assert.DoesNotContain("secret-key", exception.Message);
	}
}
