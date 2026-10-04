namespace ApiWeld.Http;

/// <summary>A response whose media type names a version other than the one the request asked for.</summary>
public sealed class MediaTypeMismatchException(HttpMethod method, string template, string requested, string received)
	: Exception($"{method} {template} asked for version {requested} but the response is version {received}.")
{
	/// <summary>The normalized version the request asked for.</summary>
	public string Requested { get; } = requested;

	/// <summary>The normalized version the response carries.</summary>
	public string Received { get; } = received;
}
