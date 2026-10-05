using Microsoft.Extensions.Logging;

namespace ApiWeld.Http;

/// <summary>The base every generated client's root partial class derives from.</summary>
public abstract class ApiClient
{
	/// <summary>A client over <paramref name="http"/>.</summary>
	protected ApiClient(HttpClient http, ApiClientOptions options, ILogger? logger = null)
		=> Transport = new ApiTransport(http, options, logger);

	/// <summary>The transport generated navigation hands every request to.</summary>
	protected ApiTransport Transport { get; }
}

/// <summary>The options bound for one client type, so several clients can be configured side by side.</summary>
public sealed class ApiClientOptions<TClient> : ApiClientOptions where TClient : ApiClient;
