namespace ApiWeld.Generator.Model;

/// <summary>Which way a body travels; types merge only within one direction.</summary>
internal enum Direction { Response, Request, Error }

/// <summary>What a model type is emitted as.</summary>
internal enum ModelKind { Object, Enum }

/// <summary>The C# type of a value.</summary>
internal abstract record TypeRef;

/// <summary>A built-in C# type.</summary>
internal sealed record ScalarRef(string Name, bool IsValueType) : TypeRef
{
	public static readonly ScalarRef String = new("string", false);
	public static readonly ScalarRef Guid = new("Guid", true);
	public static readonly ScalarRef DateTimeOffset = new("DateTimeOffset", true);
	public static readonly ScalarRef DateOnly = new("DateOnly", true);
	public static readonly ScalarRef Int = new("int", true);
	public static readonly ScalarRef Long = new("long", true);
	public static readonly ScalarRef Decimal = new("decimal", true);
	public static readonly ScalarRef Bool = new("bool", true);
	public static readonly ScalarRef Json = new("JsonElement", true);
}

/// <summary>A <c>List&lt;T&gt;</c>.</summary>
internal sealed record ListRef(TypeRef Item) : TypeRef;

/// <summary>A <c>Dictionary&lt;string, T&gt;</c>.</summary>
internal sealed record MapRef(TypeRef Value) : TypeRef;

/// <summary>A generated model type.</summary>
internal sealed record ModelRef(ModelType Model) : TypeRef;

/// <summary>One property of an object model.</summary>
internal sealed record ModelProperty(string JsonName, TypeRef Type, string? Description, bool Required);

/// <summary>A name a model could take: plain, and with its operation's verb inserted after the version.</summary>
internal sealed record NameCandidate(string Plain, string Qualified, bool FromGet, string Source);

/// <summary>One generated type: a shape travelling in one direction, with every name it could take.</summary>
internal sealed class ModelType(string shapeKey, Direction direction, ModelKind kind)
{
	public string ShapeKey { get; } = shapeKey;

	public Direction Direction { get; } = direction;

	public ModelKind Kind { get; } = kind;

	public List<ModelProperty> Properties { get; } = [];

	public List<string> EnumValues { get; } = [];

	public List<NameCandidate> Candidates { get; } = [];

	public string? Summary { get; set; }

	public string Name { get; set; } = "";

	public NameCandidate? Chosen { get; set; }
}
