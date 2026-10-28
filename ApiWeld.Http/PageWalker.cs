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
	/// <summary>Walks every page, sending <paramref name="limit"/> if set and expecting <paramref name="full"/> rows a page, or what the first page holds.</summary>
	public static async IAsyncEnumerable<T> WalkAsync<T>(ApiOperation operation, PageFetcher<T> fetch, int? limit, int? full, int narrowing, int cap,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var paging = operation.Paging!;
		var failures = new List<PageFailure>();
		var requests = new Requests(cap);
		var offset = 0;
		int? size = null;
		int? total = null;
		string? previous = null;
		var walking = true;
		var lost = false;

		fetch = requests.Counted(fetch);

		while (walking)
		{
			var asked = Asked(paging, offset, limit, full);
			Step<T> step;

			try
			{
				step = await StepAsync(fetch, offset, asked, size, total, narrowing, paging.Limit is not null, requests, failures, cancellationToken).ConfigureAwait(false);
			}
			catch (CapReachedException)
			{
				failures.Add(Capped(offset, total, cap));
				break;
			}

			if (step.Page is { } page)
			{
				if (page.Items.Count == 0)
					break;

				bool repeated;

				try
				{
					repeated = await GuardAsync(page, previous, offset, asked, fetch, paging.Undeclared, cancellationToken).ConfigureAwait(false);
				}
				catch (CapReachedException)
				{
					failures.Add(Capped(offset, total, cap));
					break;
				}

				if (repeated)
					break;

				foreach (var item in page.Items)
					yield return item;

				// The page before a failure is all the walk knows of the server's page size.
				size = page.Items.Count;
				total = page.Total;
				offset += page.Items.Count;
				previous = page.Body;
				lost = false;
				full = Grown(full, limit, page.Items.Count);

				// A page longer than the limit sent means the server ignored it and does not page; it is the whole answer.
				var ignored = page.Items.Count > asked;

				// Without a total, a short page is the end.
				walking = !ignored && (total is { } count ? offset < count : page.Items.Count == full);
			}
			else
			{
				foreach (var item in step.Rows)
					yield return item;

				// The window is stepped over whole: its good records are in hand and its bad ones are reported.
				offset += step.Span;
				previous = null;

				// With a total, more rows are known to exist; without one, a second window in a row with no rows is taken as the end.
				walking = total is { } count ? offset < count : !(lost && step.Rows.Count == 0);
				lost = step.Rows.Count == 0;
			}
		}

		if (failures.Count > 0)
			throw new ApiPagingException(operation, failures);
	}

	/// <summary>The rows a walk never reached because it ran out of requests: up to the total, or, without one, an unknown extent.</summary>
	static PageFailure Capped(int offset, int? total, int cap)
		=> new(offset, total is { } count ? Math.Max(count - offset, 0) : 0,
			new InvalidOperationException($"The walk made its {cap} requests (ApiClientOptions.MaxPageRequests) before reaching the end of the rows."));

	/// <summary>The limit to ask a window for: the one sent, or, with no offset to move, the page size once a page has shown it.</summary>
	public static int? Asked(ApiPaging paging, int offset, int? limit, int? full)
		=> limit ?? (paging.Offset is null && offset > 0 ? full : null);

	/// <summary>The full-page size after a page: the first page sets it, and with no limit sent a longer page shows the server's is larger.</summary>
	public static int Grown(int? full, int? limit, int count) => full is { } known && (limit is not null || count <= known) ? known : count;

	/// <summary>One turn of the walk: a page, or the rows recovered from a failed window and how many records it covered.</summary>
	readonly record struct Step<T>(PageFetch<T>? Page, List<T> Rows, int Span);

	/// <summary>One narrowing request: the rows it fetched, or the failure it hit, or neither when the server ignored the limit.</summary>
	readonly record struct Attempt<T>(List<T>? Rows, Exception? Error, bool Ignored);

	/// <summary>The next page; or, when a failure can be narrowed, the records of that window the server will still answer for.</summary>
	static async Task<Step<T>> StepAsync<T>(PageFetcher<T> fetch, int offset, int? limit, int? size, int? total, int narrowing, bool splittable,
		Requests requests, List<PageFailure> failures, CancellationToken cancellationToken)
	{
		try
		{
			return new(await fetch(offset, limit, cancellationToken).ConfigureAwait(false), [], 0);
		}
		// Narrowing needs a window to narrow, the size of the page before; so it only starts past the first page.
		catch (Exception error) when (Narrowable(error) && narrowing > 0 && size is { } window)
		{
			var span = total is { } count ? Math.Clamp(count - offset, 1, window) : window;
			var rows = new List<T>();

			// With no limit parameter, half a window cannot be asked for, so the window is reported whole.
			if (splittable)
				await NarrowAsync(fetch, offset, span, error, rows, failures, new Budget(narrowing, requests), cancellationToken).ConfigureAwait(false);
			else
				failures.Add(new(offset, span, error));

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

	/// <summary>The extra requests one failed window may spend narrowing itself, within what the walk has left.</summary>
	sealed class Budget(int requests, Requests walk)
	{
		int remaining = requests;

		/// <summary>Spends <paramref name="count"/> requests, or refuses when that many are not left here or in the walk.</summary>
		public bool TrySpend(int count)
		{
			if (remaining < count || !walk.Left(count))
				return false;

			remaining -= count;

			return true;
		}
	}

	/// <summary>Every request one walk makes, held to its cap; a cap of 0 is none.</summary>
	sealed class Requests(int cap)
	{
		int made;

		/// <summary>Whether <paramref name="count"/> more requests fit under the cap.</summary>
		public bool Left(int count) => cap <= 0 || made + count <= cap;

		/// <summary>The fetch, counting each request and refusing one past the cap.</summary>
		public PageFetcher<T> Counted<T>(PageFetcher<T> fetch) => (offset, limit, cancellationToken) =>
		{
			if (!Left(1))
				throw new CapReachedException();

			made++;

			return fetch(offset, limit, cancellationToken);
		};
	}

	/// <summary>A walk out of requests; never narrowed, and caught by the walk itself.</summary>
	sealed class CapReachedException : Exception;

	/// <summary>
	/// Checks for a server ignoring the offset: a page that repeats the one before it, as does the page one row later.
	/// Throws when it is; returns true instead when the offset was never declared, since the pages before were then everything.
	/// </summary>
	public static async Task<bool> GuardAsync<T>(PageFetch<T> page, string? previous, int offset, int? limit,
		PageFetcher<T> fetch, bool undeclared, CancellationToken cancellationToken)
	{
		if (previous is null || page.Items.Count == 0 || page.Body != previous)
			return false;

		// Rows can legitimately repeat; a server that honors the offset still answers a shifted request differently.
		var shifted = await fetch(offset + 1, limit, cancellationToken).ConfigureAwait(false);

		if (shifted.Body != page.Body)
			return false;

		return undeclared
			? true
			: throw new InvalidOperationException($"The page at offset {offset} repeats the page before it, and so does the page at offset {offset + 1}; the server appears to ignore the offset parameter.");
	}
}
