namespace ApiWeld.Http;

/// <summary>How a generated client reaches its API; bound from configuration.</summary>
/// <remarks>See README.md, "Registration".</remarks>
public class ApiClientOptions
{
	/// <summary>The API's base address. Required.</summary>
	public Uri? BaseUrl { get; set; }

	/// <summary>Per-request timeout.</summary>
	public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

	/// <summary>How long a pooled connection lives before it is replaced, so DNS changes are picked up.</summary>
	public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(2);

	/// <summary>What happens when a response carries a different version than was asked for.</summary>
	public VersionMismatchBehavior VersionMismatch { get; set; } = VersionMismatchBehavior.Throw;

	/// <summary>Trades a key for a bearer token before each request; off when null.</summary>
	public TokenExchangeOptions? TokenExchange { get; set; }
}

/// <summary>What the runtime does with a response whose version differs from the request's.</summary>
public enum VersionMismatchBehavior
{
	/// <summary>Throw <see cref="MediaTypeMismatchException"/>.</summary>
	Throw,

	/// <summary>Log a warning and read the response anyway.</summary>
	Warn
}
