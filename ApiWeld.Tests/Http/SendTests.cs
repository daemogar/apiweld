using System.Net;
using System.Text.Json.Serialization;

using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class SendTests
{
	sealed class Widget
	{
		[JsonPropertyName("name")] public string? Name { get; set; }
	}

	sealed class Problem
	{
		[JsonPropertyName("code")] public string? Code { get; set; }
	}

	static readonly ApiOperation Get = new(HttpMethod.Get, "api/widgets/{id}")
	{
		Accept = "application/vnd.example.v2+json",
		Version = "2",
		Errors = new Dictionary<int, ApiErrorFactory> { [400] = ApiResponseException<Problem>.Create }
	};

	static ApiTransport Transport(FakeHandler handler, ApiClientOptions? options = null, ListLogger? logger = null)
		=> new(handler.Client(), options ?? new ApiClientOptions(), logger);

	static Task<Widget?> Send(ApiTransport transport)
		=> transport.SendAsync<Widget>(Get, transport.CreateRequest(Get, ["1"]));

	[Fact]
	public async Task Reads_a_success_body()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "name": "w" }""", "application/vnd.example.v2+json"));

		var widget = await Send(Transport(handler));

		Assert.Equal("w", widget?.Name);
		Assert.Equal("https://api.example.test/api/widgets/1", handler.Requests.Single().Request.RequestUri!.ToString());
	}

	[Fact]
	public async Task Returns_nothing_for_an_empty_success()
	{
		var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));

		Assert.Null(await Send(Transport(handler)));
	}

	[Fact]
	public async Task Throws_the_typed_error_for_a_declared_status()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "code": "E1" }""", status: HttpStatusCode.BadRequest));

		var exception = await Assert.ThrowsAsync<ApiResponseException<Problem>>(() => Send(Transport(handler)));

		Assert.Equal("E1", exception.Error?.Code);
	}

	[Fact]
	public async Task Throws_the_untyped_error_for_an_undeclared_status()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("boom", status: HttpStatusCode.InternalServerError));

		var exception = await Assert.ThrowsAsync<ApiResponseException>(() => Send(Transport(handler)));

		Assert.Equal(typeof(ApiResponseException), exception.GetType());
		Assert.Equal("boom", exception.Body);
	}

	[Fact]
	public async Task Throws_when_the_response_names_another_version()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("{}", "application/vnd.example.v3+json"));

		var exception = await Assert.ThrowsAsync<MediaTypeMismatchException>(() => Send(Transport(handler)));

		Assert.Equal(("2", "3"), (exception.Requested, exception.Received));
	}

	[Fact]
	public async Task Accepts_the_same_version_written_differently()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "name": "w" }""", "application/vnd.example.v2.0.0+json"));

		Assert.Equal("w", (await Send(Transport(handler)))?.Name);
	}

	[Fact]
	public async Task Ignores_a_response_that_names_no_version()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "name": "w" }"""));

		Assert.Equal("w", (await Send(Transport(handler)))?.Name);
	}

	[Fact]
	public async Task Warns_instead_of_throwing_when_configured()
	{
		var handler = new FakeHandler((_, _) => FakeHandler.Json("""{ "name": "w" }""", "application/vnd.example.v3+json"));
		var logger = new ListLogger();

		var widget = await Send(Transport(handler, new ApiClientOptions { VersionMismatch = VersionMismatchBehavior.Warn }, logger));

		Assert.Equal("w", widget?.Name);
		Assert.Equal("Warning: GET api/widgets/{id} asked for version 2 but the response is version 3.", Assert.Single(logger.Messages));
	}

	[Fact]
	public void Gives_a_derived_client_its_transport()
	{
		var client = new SampleClient(new HttpClient(), new ApiClientOptions<SampleClient>());

		Assert.NotNull(client.ExposedTransport);
	}

	sealed class SampleClient(HttpClient http, ApiClientOptions<SampleClient> options) : ApiClient(http, options)
	{
		public ApiTransport ExposedTransport => Transport;
	}
}
