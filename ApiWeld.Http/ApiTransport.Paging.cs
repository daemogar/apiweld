using System.Globalization;
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
		var paging = RequirePaging(operation);
		var sent = Sent(paging, query, null);

		return PageWalker.WalkAsync<T>(operation,
			(offset, limit, token) => FetchPageAsync<T>(operation, path, query, offset, limit, token),
			sent,
			Full(paging, sent),
			ClientOptions.PageNarrowingRequests,
			ClientOptions.MaxPageRequests,
			cancellationToken);
	}

	/// <summary>One page of a paged operation.</summary>
	public async Task<Page<T>> GetPageAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, int offset, int? limit, CancellationToken cancellationToken = default)
	{
		var paging = RequirePaging(operation);

		PageFetcher<T> fetch = (at, size, token) => FetchPageAsync<T>(operation, path, query, at, size, token);
		var sent = Sent(paging, query, limit);

		return new(await fetch(offset, PageWalker.Asked(paging, offset, sent, Full(paging, sent)), cancellationToken).ConfigureAwait(false),
			offset, sent, fetch, Full(paging, sent), paging);
	}

	static ApiPaging RequirePaging(ApiOperation operation)
		=> operation.Paging ?? throw new InvalidOperationException($"{operation.Method} {operation.Template} is not a paged operation.");

	/// <summary>The limit to send: the one given or the caller's on the query, held to the maximum; null when there is none.</summary>
	static int? Sent(ApiPaging paging, IApiQuery? query, int? limit)
	{
		if (paging.Limit is not { } name)
			return null;

		if (limit is null)
		{
			var parameters = new ApiRequestParameters();
			query?.Apply(parameters);
			var set = parameters.QueryValues.FirstOrDefault(pair => pair.Key == name).Value;

			limit = int.TryParse(set, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
		}

		return limit is { } size ? Held(paging, size) : null;
	}

	/// <summary>The size of a full page: the limit sent, else the described default; null when only the first page can tell.</summary>
	static int? Full(ApiPaging paging, int? sent) => sent ?? (paging.Limit is not null && paging.DefaultLimit is { } size ? Held(paging, size) : null);

	static int Held(ApiPaging paging, int limit) => paging.MaximumLimit is { } maximum ? Math.Min(limit, maximum) : limit;

	async Task<PageFetch<T>> FetchPageAsync<T>(ApiOperation operation, IReadOnlyList<string> path, IApiQuery? query, int offset, int? limit, CancellationToken cancellationToken)
	{
		var paging = RequirePaging(operation);
		var parameters = new ApiRequestParameters();
		query?.Apply(parameters);

		// With no offset parameter, a window is every row up to its end, less the rows before it.
		var skip = paging.Offset is null ? offset : 0;

		if (paging.Offset is { } offsetName)
			parameters.Query(offsetName, offset);

		if (paging.Limit is { } limitName && limit is not null)
			parameters.Query(limitName, skip + limit);

		var exchange = await ExchangeAsync(operation, CreateRequest(operation, path, parameters, null), cancellationToken).ConfigureAwait(false);
		var items = string.IsNullOrWhiteSpace(exchange.Body) ? [] : JsonSerializer.Deserialize<List<T>>(exchange.Body, Json) ?? [];

		return new(skip > 0 ? items[Math.Min(skip, items.Count)..] : items, exchange.Total, exchange.Body);
	}
}
