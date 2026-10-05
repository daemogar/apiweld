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

			if (previous is not null && page.Body == previous)
				throw Repeated(offset);

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

	public static InvalidOperationException Repeated(int offset)
		=> new($"The page at offset {offset} repeats the page before it; the server appears to ignore the offset parameter.");
}
