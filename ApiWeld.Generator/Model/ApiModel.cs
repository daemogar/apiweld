namespace ApiWeld.Generator.Model;

/// <summary>One segment of the shared path tree.</summary>
internal sealed class PathNode(string segment, bool isParameter, PathNode? parent)
{
	public string Segment { get; } = segment;

	public bool IsParameter { get; } = isParameter;

	public PathNode? Parent { get; } = parent;

	public string ParameterName { get; set; } = "";

	public string ParameterType { get; set; } = "string";

	public bool TypeConflict { get; set; }

	public SortedDictionary<string, PathNode> Children { get; } = new(StringComparer.Ordinal);

	public SortedDictionary<string, List<OperationModel>> Versions { get; } = new(StringComparer.Ordinal);

	/// <summary>The stem of this node's generated class names: segments in PascalCase, parameters as <c>Item</c>.</summary>
	public string ClassBase => Parent is null ? "" : Parent.ClassBase + (IsParameter ? "Item" : Words.Pascal(Segment));

	/// <summary>The path as a reader would write it, after base paths are removed.</summary>
	public string Display => Parent is null ? "" : Parent.Display + "/" + (IsParameter ? "{" + ParameterName + "}" : Segment);

	public PathNode Child(string segment, bool isParameter)
	{
		var key = isParameter ? "{}" : segment;

		if (!Children.TryGetValue(key, out var child))
			Children.Add(key, child = new PathNode(segment, isParameter, this));

		return child;
	}

	public IEnumerable<PathNode> Descendants() => Children.Values.SelectMany(child => child.Descendants().Prepend(child));
}

/// <summary>A query or header parameter an operation's query object carries.</summary>
internal sealed record QueryParameter(string Name, string In, TypeRef Type, string? Description, bool Required);

/// <summary>One operation at one version on one path node.</summary>
internal sealed class OperationModel
{
	public required string Method { get; init; }

	public required string Template { get; init; }

	public required string? Version { get; init; }

	public required string VersionMember { get; init; }

	public required string Source { get; init; }

	public string? MediaType { get; init; }

	public string? Accept { get; init; }

	public string? ContentType { get; init; }

	public TypeRef? Response { get; init; }

	public TypeRef? Request { get; init; }

	public bool IsPaged { get; init; }

	public List<QueryParameter> Parameters { get; init; } = [];

	public SortedDictionary<int, TypeRef> Errors { get; init; } = new();
}

/// <summary>Everything generated from a set of descriptions.</summary>
internal sealed record ApiModel(PathNode Root, IReadOnlyList<ModelType> Models);
