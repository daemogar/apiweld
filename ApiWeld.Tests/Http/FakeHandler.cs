using System.Net;
using System.Net.Http.Headers;

namespace ApiWeld.Tests.Http;

/// <summary>An in-memory server: answers each request with a function and records what was sent.</summary>
sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
	public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
		Requests.Add((request, body));

		return respond(request, body);
	}

	public HttpClient Client() => new(this) { BaseAddress = new Uri("https://api.example.test/") };

	public static HttpResponseMessage Json(string json, string mediaType = "application/json", HttpStatusCode status = HttpStatusCode.OK)
	{
		var response = new HttpResponseMessage(status) { Content = new StringContent(json) };
		response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);

		return response;
	}
}
