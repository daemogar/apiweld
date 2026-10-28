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

	/// <summary>
	/// Sends the error media types in <c>Accept</c> after the success one. Off by default, since some
	/// servers refuse any <c>Accept</c> with more than one value. See README.md, "Version checking".
	/// </summary>
	public bool AcceptErrorMediaTypes { get; set; }

	/// <summary>
	/// Extra requests one failed page of a walk may spend narrowing the failure to the record behind it;
	/// 0 turns narrowing off, and the failure is thrown as it is. Each halving costs two, so 32 narrows a
	/// page of up to 65,536 records. See README.md, "Page walking".
	/// </summary>
	public int PageNarrowingRequests { get; set; } = 32;

	/// <summary>
	/// The most requests one page walk may make, narrowing included, before it ends and reports the rest
	/// as not fetched; 0 removes the cap. See README.md, "Page walking".
	/// </summary>
	public int MaxPageRequests { get; set; } = 1000;

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
