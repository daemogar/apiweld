namespace ApiWeld.Generator.Model;

/// <summary>Where a schema sits, which is what its type's candidate name is built from.</summary>
internal sealed record NameContext(
	string Base, string Version, string Verb, IReadOnlyList<string> Path, string Suffix,
	Direction Direction, bool FromGet, string Source, bool ErrorRoot = false)
{
	/// <summary>The context of a property named <paramref name="part"/>.</summary>
	public NameContext Child(string part) => this with { Path = [.. Path, part], ErrorRoot = false };

	/// <summary>The context of an array's items: the last path part made singular.</summary>
	public NameContext Item() => Path.Count == 0
		? this
		: this with { Path = [.. Path.Take(Path.Count - 1), Words.SingularPascal(Path[^1])], ErrorRoot = false };

	/// <summary>A context whose base is <paramref name="base"/> and whose path starts again.</summary>
	public NameContext Rebase(string @base) => this with { Base = @base, Path = [], ErrorRoot = false };

	/// <summary>The candidate name this context gives a type.</summary>
	public NameCandidate Candidate()
	{
		var path = string.Concat(Path);

		return new(Base + Version + path + Suffix, Base + Version + Verb + path + Suffix, FromGet, Source);
	}
}
