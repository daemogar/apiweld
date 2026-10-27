namespace ApiWeld.Http;

/// <summary>A record, or a range of records, a paged walk could not fetch.</summary>
/// <remarks>See README.md, "Page walking".</remarks>
public sealed record PageFailure(int Offset, int Count, Exception Error)
{
	/// <summary>Whether the failure was narrowed to the one record at <see cref="Offset"/>.</summary>
	public bool IsRecord => Count == 1;

	/// <summary>The last record the failure covers.</summary>
	public int Last => Offset + Count - 1;

	/// <summary>The record or range, as the exception message names it.</summary>
	public override string ToString() => IsRecord ? $"record {Offset}" : $"records {Offset}-{Last}";
}

/// <summary>A paged walk that returned its other records but could not fetch every one of them.</summary>
/// <remarks>See README.md, "Page walking".</remarks>
public class ApiPagingException : Exception
{
	internal ApiPagingException(ApiOperation operation, IReadOnlyList<PageFailure> failures)
		: base($"{operation.Method} {operation.Template} returned every record except {string.Join(", ", failures)}.", failures[0].Error)
		=> Failures = failures;

	/// <summary>Every record or range the walk could not fetch, in the order it met them.</summary>
	public IReadOnlyList<PageFailure> Failures { get; }
}

/// <summary>A paged walk gathered into a list, carrying the rows it did fetch.</summary>
/// <remarks>See README.md, "Page walking".</remarks>
public sealed class ApiPagingException<T> : ApiPagingException
{
	internal ApiPagingException(ApiOperation operation, IReadOnlyList<PageFailure> failures, IReadOnlyList<T> rows)
		: base(operation, failures)
		=> Rows = rows;

	/// <summary>The rows the walk did fetch, the failed records aside.</summary>
	public IReadOnlyList<T> Rows { get; }
}
