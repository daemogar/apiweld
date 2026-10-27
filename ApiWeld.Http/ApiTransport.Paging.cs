using System.Text.Json;

namespace ApiWeld.Http;

public sealed partial class ApiTransport
{
	/// <summary>Every row of a paged operation, walking pages as the README's "Page walking" describes.</summary>
	/// <exception cref="ApiPagingException{T}">A record the walk could not fetch; its <c>Rows</c> are every other row.</exception>
	public async Task<List<T>> GetAllAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, CancellationToken cancellationToken = default)
	{
		var rows = new List<T>();

		try
		{
			await foreach (var row in EnumerateAsync<T>(operation, path, query, cancellationToken).ConfigureAwait(false))
				rows.Add(row);
		}
		catch (ApiPagingException failure)
		{
			throw new ApiPagingException<T>(operation, failure.Failures, rows);
		}

		return rows;
	}

	/// <summary>The rows of a paged operation, fetching each page only when the caller reaches it.</summary>
	/// <exception cref="ApiPagingException">A record the walk could not fetch, thrown once every other row has been yielded.</exception>
	public IAsyncEnumerable<T> EnumerateAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, CancellationToken cancellationToken = default)
	{
		RequirePaging(operation);

		return PageWalker.WalkAsync<T>(operation,
			(offset, limit, token) => FetchPageAsync<T>(operation, path, query, offset, limit, token),
			ClientOptions.PageNarrowingRequests,
			cancellationToken);
	}

	/// <summary>One page of a paged operation.</summary>
	public async Task<Page<T>> GetPageAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, int offset, int? limit, CancellationToken cancellationToken = default)
	{
		RequirePaging(operation);

		PageFetcher<T> fetch = (at, size, token) => FetchPageAsync<T>(operation, path, query, at, size, token);

		return new(await fetch(offset, limit, cancellationToken).ConfigureAwait(false), offset, limit, fetch);
	}

	static ApiPaging RequirePaging(ApiOperation operation)
		=> operation.Paging ?? throw new InvalidOperationException($"{operation.Method} {operation.Template} is not a paged operation.");

	async Task<PageFetch<T>> FetchPageAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, int offset, int? limit, CancellationToken cancellationToken)
	{
		var paging = RequirePaging(operation);
		var parameters = new ApiRequestParameters();
		query?.Apply(parameters);
		parameters.Query(paging.Offset, offset);

		if (limit is not null)
			parameters.Query(paging.Limit, limit);

		var exchange = await ExchangeAsync(operation, CreateRequest(operation, path, parameters, null), cancellationToken).ConfigureAwait(false);
		var items = string.IsNullOrWhiteSpace(exchange.Body) ? [] : JsonSerializer.Deserialize<List<T>>(exchange.Body, Json) ?? [];

		return new(items, exchange.Total, exchange.Body);
	}
}
