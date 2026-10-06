using System.Runtime.CompilerServices;

namespace ApiWeld.Http;

/// <summary>One fetched page: its rows, the total header if any, and the raw body for the repeat guard.</summary>
internal sealed record PageFetch<T>(List<T> Items, int? Total, string Body);

/// <summary>Walks a paged operation from offset zero; the one implementation behind every paged call.</summary>
/// <remarks>See README.md, "Page walking".</remarks>
internal static class PageWalker
{
	public static async IAsyncEnumerable<T> WalkAsync<T>(
		Func<int, CancellationToken, Task<PageFetch<T>>> fetch,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var offset = 0;
		string? previous = null;

		while (true)
		{
			var page = await fetch(offset, cancellationToken).ConfigureAwait(false);

			if (page.Items.Count == 0)
				yield break;

			await GuardAsync(page, previous, offset, fetch, cancellationToken).ConfigureAwait(false);

			foreach (var item in page.Items)
				yield return item;

			if (page.Total is not { } total)
				yield break;

			offset += page.Items.Count;

			if (offset >= total)
				yield break;

			previous = page.Body;
		}
	}

	/// <summary>Throws when a page repeats the one before it and the page one row later repeats it too.</summary>
	public static async Task GuardAsync<T>(PageFetch<T> page, string? previous, int offset,
		Func<int, CancellationToken, Task<PageFetch<T>>> fetch, CancellationToken cancellationToken)
	{
		if (previous is null || page.Items.Count == 0 || page.Body != previous)
			return;

		// Rows can legitimately repeat; a server that honors the offset still answers a shifted request differently.
		var shifted = await fetch(offset + 1, cancellationToken).ConfigureAwait(false);

		if (shifted.Body == page.Body)
			throw new InvalidOperationException($"The page at offset {offset} repeats the page before it, and so does the page at offset {offset + 1}; the server appears to ignore the offset parameter.");
	}
}
