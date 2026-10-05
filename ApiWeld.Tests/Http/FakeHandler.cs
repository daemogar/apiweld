using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ApiWeld.Tests.Http;

/// <summary>An in-memory server: answers each request with a function and records what was sent.</summary>
sealed class FakeHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
	public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		string? body = null;

		if (request.Content is not null)
		{
			// Streams the content the way a socket handler does, without buffering it.
			using var stream = new MemoryStream();
			await request.Content.CopyToAsync(stream, cancellationToken);
			body = Encoding.UTF8.GetString(stream.ToArray());
		}

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
