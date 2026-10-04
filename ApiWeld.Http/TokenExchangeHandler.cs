using System.Net;
using System.Net.Http.Headers;

namespace ApiWeld.Http;

/// <summary>Adds a bearer token to each request, and on a 401 refreshes it and retries once.</summary>
public sealed class TokenExchangeHandler(TokenSource source) : DelegatingHandler
{
	/// <inheritdoc/>
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (request.Content is not null)
			await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

		var token = await source.GetAsync(cancellationToken).ConfigureAwait(false);
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

		if (response.StatusCode != HttpStatusCode.Unauthorized)
			return response;

		response.Dispose();
		source.Invalidate(token);

		var retry = await CloneAsync(request, cancellationToken).ConfigureAwait(false);
		retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await source.GetAsync(cancellationToken).ConfigureAwait(false));

		return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
	}

	static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var clone = new HttpRequestMessage(request.Method, request.RequestUri)
		{
			Version = request.Version,
			VersionPolicy = request.VersionPolicy
		};

		foreach (var header in request.Headers)
			clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

		if (request.Content is not null)
		{
			clone.Content = new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false));

			foreach (var header in request.Content.Headers)
				clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
		}

		foreach (var option in request.Options)
			((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;

		return clone;
	}
}
