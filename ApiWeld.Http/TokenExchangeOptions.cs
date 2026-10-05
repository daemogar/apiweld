namespace ApiWeld.Http;

/// <summary>Settings for trading an API key for a bearer token.</summary>
public sealed class TokenExchangeOptions
{
	/// <summary>Where the key is posted, relative to the base URL. Required.</summary>
	public string? Endpoint { get; set; }

	/// <summary>The key sent to <see cref="Endpoint"/>. Required.</summary>
	public string? ApiKey { get; set; }

	/// <summary>The authorization scheme the key is sent under.</summary>
	public string Scheme { get; set; } = "Bearer";

	/// <summary>How the token is read from the exchange response.</summary>
	public TokenFormat TokenFormat { get; set; } = TokenFormat.RawText;

	/// <summary>The property holding the token when <see cref="TokenFormat"/> is <see cref="TokenFormat.JsonProperty"/>.</summary>
	public string? TokenProperty { get; set; }

	/// <summary>How long before a token's expiry it is replaced.</summary>
	public TimeSpan RefreshMargin { get; set; } = TimeSpan.FromSeconds(30);

	/// <summary>How long a token without a readable expiry is kept.</summary>
	public TimeSpan FallbackLifetime { get; set; } = TimeSpan.FromMinutes(4);
}

/// <summary>The shape of a token exchange response.</summary>
public enum TokenFormat
{
	/// <summary>The whole body, trimmed, is the token.</summary>
	RawText,

	/// <summary>The body is a JSON object; the token is one of its string properties.</summary>
	JsonProperty
}
