namespace ApiWeld.Http;

/// <summary>The query and header names a paged operation uses.</summary>
public sealed record ApiPaging(string Offset, string Limit, string TotalHeader);

/// <summary>One generated operation, described as data.</summary>
public sealed record ApiOperation(HttpMethod Method, string Template)
{
	/// <summary>The <c>Accept</c> header value: the pinned success media type plus the error media types.</summary>
	public string? Accept { get; init; }

	/// <summary>The request body's media type.</summary>
	public string? ContentType { get; init; }

	/// <summary>The normalized version the operation asks for, or null.</summary>
	public string? Version { get; init; }

	/// <summary>The paging convention, when the operation is paged.</summary>
	public ApiPaging? Paging { get; init; }

	/// <summary>Exception factories by declared error status.</summary>
	public IReadOnlyDictionary<int, ApiErrorFactory> Errors { get; init; } = new Dictionary<int, ApiErrorFactory>();

	/// <summary>Exception factories by declared status range, keyed by its first digit: 4 for <c>4XX</c>.</summary>
	public IReadOnlyDictionary<int, ApiErrorFactory> ErrorRanges { get; init; } = new Dictionary<int, ApiErrorFactory>();

	/// <summary>The factory for a status no code or range declares, from the <c>default</c> response.</summary>
	public ApiErrorFactory? DefaultError { get; init; }
}
