namespace ApiWeld.Http;

/// <summary>One page of a paged operation, able to fetch the page after it.</summary>
public sealed class Page<T>
{
	readonly PageFetcher<T> fetch;
	readonly string body;
	readonly int full;
	readonly ApiPaging paging;

	internal Page(PageFetch<T> page, int offset, int? limit, PageFetcher<T> fetch, int? full, ApiPaging paging)
	{
		Items = page.Items;
		Total = page.Total;
		Offset = offset;
		Limit = limit;
		body = page.Body;
		this.fetch = fetch;
		this.full = PageWalker.Grown(full, limit, page.Items.Count);
		this.paging = paging;
	}

	/// <summary>This page's rows.</summary>
	public IReadOnlyList<T> Items { get; }

	/// <summary>The offset this page starts at.</summary>
	public int Offset { get; }

	/// <summary>The limit that was sent, or null when the server's default applied.</summary>
	public int? Limit { get; }

	/// <summary>The total the server reported, or null when it sent no total header.</summary>
	public int? Total { get; }

	/// <summary>Whether rows remain: none when the page is longer than the limit sent; else by the total, or by whether the page came back full.</summary>
	public bool HasMore => Items.Count > 0 && !(Items.Count > Limit)
		&& (Total is { } total ? Offset + Items.Count < total : Items.Count == full);

	/// <summary>The next page, or null when there is none. A page fetched by hand is never narrowed; a failure throws.</summary>
	public async Task<Page<T>?> NextAsync(CancellationToken cancellationToken = default)
	{
		if (!HasMore)
			return null;

		var offset = Offset + Items.Count;
		var asked = PageWalker.Asked(paging, offset, Limit, full);
		var page = await fetch(offset, asked, cancellationToken).ConfigureAwait(false);

		if (await PageWalker.GuardAsync(page, body, offset, asked, fetch, paging.Undeclared, cancellationToken).ConfigureAwait(false))
			return null;

		return new(page, offset, Limit, fetch, full, paging);
	}
}
