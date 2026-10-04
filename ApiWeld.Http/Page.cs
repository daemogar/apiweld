namespace ApiWeld.Http;

/// <summary>One page of a paged operation, able to fetch the page after it.</summary>
public sealed class Page<T>
{
	readonly Func<int, CancellationToken, Task<PageFetch<T>>> fetch;
	readonly string body;

	internal Page(PageFetch<T> page, int offset, int? limit, Func<int, CancellationToken, Task<PageFetch<T>>> fetch)
	{
		Items = page.Items;
		Total = page.Total;
		Offset = offset;
		Limit = limit;
		body = page.Body;
		this.fetch = fetch;
	}

	/// <summary>This page's rows.</summary>
	public IReadOnlyList<T> Items { get; }

	/// <summary>The offset this page starts at.</summary>
	public int Offset { get; }

	/// <summary>The limit that was sent, or null when the server's default applied.</summary>
	public int? Limit { get; }

	/// <summary>The total the server reported, or null when it sent no total header.</summary>
	public int? Total { get; }

	/// <summary>Whether the server's total says rows remain after this page.</summary>
	public bool HasMore => Total is { } total && Items.Count > 0 && Offset + Items.Count < total;

	/// <summary>The next page, or null when there is none.</summary>
	public async Task<Page<T>?> NextAsync(CancellationToken cancellationToken = default)
	{
		if (!HasMore)
			return null;

		var offset = Offset + Items.Count;
		var page = await fetch(offset, cancellationToken).ConfigureAwait(false);

		if (page.Items.Count > 0 && page.Body == body)
			throw PageWalker.Repeated(offset);

		return new(page, offset, Limit, fetch);
	}
}
