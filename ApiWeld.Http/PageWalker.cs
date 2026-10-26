using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ApiWeld.Http;

/// <summary>One fetched page: its rows, the total header if any, and the raw body for the repeat guard.</summary>
internal sealed record PageFetch<T>(List<T> Items, int? Total, string Body);

/// <summary>Fetches one window: where it starts, and the limit to send, or null for the server's own page size.</summary>
internal delegate Task<PageFetch<T>> PageFetcher<T>(int offset, int? limit, CancellationToken cancellationToken);

/// <summary>Walks a paged operation from offset zero; the one implementation behind every paged call.</summary>
/// <remarks>See README.md, "Page walking".</remarks>
internal static class PageWalker
{
	public static async IAsyncEnumerable<T> WalkAsync<T>(ApiOperation operation, PageFetcher<T> fetch, int narrowing,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var failures = new List<PageFailure>();
		var offset = 0;
		int? size = null;
		int? total = null;
		string? previous = null;
		var walking = true;

		while (walking)
		{
			var step = await StepAsync(fetch, offset, size, total, narrowing, failures, cancellationToken).ConfigureAwait(false);

			if (step.Page is { } page)
			{
				if (page.Items.Count == 0)
					break;

				await GuardAsync(page, previous, offset, null, fetch, cancellationToken).ConfigureAwait(false);

				foreach (var item in page.Items)
					yield return item;

				// The page before a failure is all the walk knows of the server's page size.
				size = page.Items.Count;
				total = page.Total;
				offset += page.Items.Count;
				previous = page.Body;
				walking = page.Total is not null;
			}
			else
			{
				foreach (var item in step.Rows)
					yield return item;

				// The window is stepped over whole: its good records are in hand and its bad ones are reported.
				offset += step.Span;
				previous = null;
			}

			walking = walking && total is { } count && offset < count;
		}

		if (failures.Count > 0)
			throw new ApiPagingException(operation, failures);
	}

	/// <summary>One turn of the walk: a page, or the rows recovered from a failed window and how many records it covered.</summary>
	readonly record struct Step<T>(PageFetch<T>? Page, List<T> Rows, int Span);

	/// <summary>One narrowing request: the rows it fetched, or the failure it hit, or neither when the server ignored the limit.</summary>
	readonly record struct Attempt<T>(List<T>? Rows, Exception? Error, bool Ignored);

	/// <summary>The next page; or, when a failure can be narrowed, the records of that window the server will still answer for.</summary>
	static async Task<Step<T>> StepAsync<T>(PageFetcher<T> fetch, int offset, int? size, int? total, int narrowing,
		List<PageFailure> failures, CancellationToken cancellationToken)
	{
		try
		{
			return new(await fetch(offset, null, cancellationToken).ConfigureAwait(false), [], 0);
		}
		// Narrowing needs a window to narrow: both the server's page size and the total are only known past the first page.
		catch (Exception error) when (Narrowable(error) && narrowing > 0 && size is { } window && total is { } count)
		{
			var span = Math.Clamp(count - offset, 1, window);
			var rows = new List<T>();

			await NarrowAsync(fetch, offset, span, error, rows, failures, new Budget(narrowing), cancellationToken).ConfigureAwait(false);

			return new(null, rows, span);
		}
	}

	/// <summary>Halves a failed window until the bad record is alone; a window that fails on both sides is reported whole.</summary>
	static async Task NarrowAsync<T>(PageFetcher<T> fetch, int offset, int count, Exception error,
		List<T> rows, List<PageFailure> failures, Budget budget, CancellationToken cancellationToken)
	{
		if (count <= 1 || !budget.TrySpend(2))
		{
			failures.Add(new(offset, count, error));

			return;
		}

		var half = count / 2;
		var left = await AttemptAsync(fetch, offset, half, cancellationToken).ConfigureAwait(false);
		var right = await AttemptAsync(fetch, offset + half, count - half, cancellationToken).ConfigureAwait(false);

		// Both halves bad, or a server that will not take a limit: this window is as narrow as the walk can say.
		if ((left.Rows is null && right.Rows is null) || left.Ignored || right.Ignored)
		{
			failures.Add(new(offset, count, error));

			return;
		}

		await HalfAsync(fetch, offset, half, left, rows, failures, budget, cancellationToken).ConfigureAwait(false);
		await HalfAsync(fetch, offset + half, count - half, right, rows, failures, budget, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Keeps a half's records, or narrows it further, so the rows stay in the order the server serves them.</summary>
	static async Task HalfAsync<T>(PageFetcher<T> fetch, int offset, int count, Attempt<T> attempt,
		List<T> rows, List<PageFailure> failures, Budget budget, CancellationToken cancellationToken)
	{
		if (attempt.Rows is { } fetched)
			rows.AddRange(fetched);
		else
			await NarrowAsync(fetch, offset, count, attempt.Error!, rows, failures, budget, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>One window, asked for by itself.</summary>
	static async Task<Attempt<T>> AttemptAsync<T>(PageFetcher<T> fetch, int offset, int count, CancellationToken cancellationToken)
	{
		try
		{
			var page = await fetch(offset, count, cancellationToken).ConfigureAwait(false);

			return page.Items.Count > count ? new Attempt<T>(null, null, true) : new Attempt<T>(page.Items, null, false);
		}
		catch (Exception error) when (Narrowable(error))
		{
			return new(null, error, false);
		}
	}

	/// <summary>Whether a failure could be one bad record: a response the server refused, or a body that would not read.</summary>
	static bool Narrowable(Exception error) => error is ApiResponseException or JsonException;

	/// <summary>The extra requests one failed window may spend narrowing itself.</summary>
	sealed class Budget(int requests)
	{
		int remaining = requests;

		/// <summary>Spends <paramref name="count"/> requests, or refuses when that many are not left.</summary>
		public bool TrySpend(int count)
		{
			if (remaining < count)
				return false;

			remaining -= count;

			return true;
		}
	}

	/// <summary>Throws when a page repeats the one before it and the page one row later repeats it too.</summary>
	public static async Task GuardAsync<T>(PageFetch<T> page, string? previous, int offset, int? limit,
		PageFetcher<T> fetch, CancellationToken cancellationToken)
	{
		if (previous is null || page.Items.Count == 0 || page.Body != previous)
			return;

		// Rows can legitimately repeat; a server that honors the offset still answers a shifted request differently.
		var shifted = await fetch(offset + 1, limit, cancellationToken).ConfigureAwait(false);

		if (shifted.Body == page.Body)
			throw new InvalidOperationException($"The page at offset {offset} repeats the page before it, and so does the page at offset {offset + 1}; the server appears to ignore the offset parameter.");
	}
}
