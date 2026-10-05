using System.Text.Json;

namespace ApiWeld.Http;

public sealed partial class ApiTransport
{
	/// <summary>Every row of a paged operation, walking pages as the README's "Page walking" describes.</summary>
	public async Task<List<T>> GetAllAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, CancellationToken cancellationToken = default)
	{
		var rows = new List<T>();

		await foreach (var row in EnumerateAsync<T>(operation, path, query, cancellationToken).ConfigureAwait(false))
			rows.Add(row);

		return rows;
	}

	/// <summary>The rows of a paged operation, fetching each page only when the caller reaches it.</summary>
	public IAsyncEnumerable<T> EnumerateAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, CancellationToken cancellationToken = default)
	{
		RequirePaging(operation);

		return PageWalker.WalkAsync<T>((offset, token) => FetchPageAsync<T>(operation, path, query, offset, null, token), cancellationToken);
	}

	/// <summary>One page of a paged operation.</summary>
	public async Task<Page<T>> GetPageAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, int offset, int? limit, CancellationToken cancellationToken = default)
	{
		RequirePaging(operation);

		Task<PageFetch<T>> Fetch(int at, CancellationToken token) => FetchPageAsync<T>(operation, path, query, at, limit, token);

		return new(await Fetch(offset, cancellationToken).ConfigureAwait(false), offset, limit, Fetch);
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
