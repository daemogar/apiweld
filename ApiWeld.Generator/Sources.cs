namespace ApiWeld.Generator;

/// <summary>One description to generate from.</summary>
/// <param name="FileName">The file's name; its stem is the resource's base name.</param>
/// <param name="Text">The description as written.</param>
public sealed record DescriptionSource(string FileName, string Text);

/// <summary>One generated C# file.</summary>
/// <param name="Path">Where it goes, relative to the output folder, with forward slashes.</param>
/// <param name="Content">Its text, with LF line endings.</param>
public sealed record GeneratedFile(string Path, string Content);
