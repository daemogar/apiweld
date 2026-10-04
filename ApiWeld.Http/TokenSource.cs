using System.Net.Http.Headers;
using System.Text.Json;

namespace ApiWeld.Http;

/// <summary>Trades an API key for a bearer token and caches it until shortly before it expires.</summary>
/// <remarks>See README.md, "Token exchange".</remarks>
public sealed class TokenSource(TokenExchangeOptions options, HttpClient exchangeClient, TimeProvider? time = null)
{
	readonly SemaphoreSlim gate = new(1, 1);
	readonly TimeProvider clock = time ?? TimeProvider.System;
	string? token;
	DateTimeOffset refreshAt;

	/// <summary>A current token, exchanging the key only when the cached one is missing or due.</summary>
	public async Task<string> GetAsync(CancellationToken cancellationToken)
	{
		await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

		try
		{
			var cached = Volatile.Read(ref token);

			if (cached is not null && clock.GetUtcNow() < refreshAt)
				return cached;

			var fresh = await ExchangeAsync(cancellationToken).ConfigureAwait(false);
			var now = clock.GetUtcNow();

			refreshAt = JwtExpiry.Read(fresh) is { } expires
				? expires - options.RefreshMargin
				: now + options.FallbackLifetime;
			Volatile.Write(ref token, fresh);

			return fresh;
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>Drops <paramref name="stale"/> if it is still the cached token, so the next call exchanges again.</summary>
	public void Invalidate(string stale) => Interlocked.CompareExchange(ref token, null, stale);

	async Task<string> ExchangeAsync(CancellationToken cancellationToken)
	{
		var endpoint = Uri.TryCreate(options.Endpoint, UriKind.Absolute, out var absolute)
			&& absolute.Scheme is "http" or "https"
				? absolute
			: new Uri(options.Endpoint!.TrimStart('/'), UriKind.Relative);

		using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
		request.Headers.Authorization = new AuthenticationHeaderValue(options.Scheme, options.ApiKey);

		using var response = await exchangeClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
			throw new TokenExchangeException($"Token exchange at {options.Endpoint} failed with {(int)response.StatusCode} {response.StatusCode}.");

		var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
		var value = options.TokenFormat == TokenFormat.JsonProperty ? ReadProperty(body) : body.Trim();

		return string.IsNullOrEmpty(value)
			? throw new TokenExchangeException($"Token exchange at {options.Endpoint} returned no token.")
			: value;
	}

	string? ReadProperty(string body)
	{
		try
		{
			using var document = JsonDocument.Parse(body);

			return document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.TryGetProperty(options.TokenProperty ?? "", out var property)
				&& property.ValueKind == JsonValueKind.String
					? property.GetString()
					: null;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
