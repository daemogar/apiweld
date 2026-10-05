using ApiWeld.Http;

namespace ApiWeld.Tests.Http;

public class MediaTypeVersionTests
{
	[Theory]
	[InlineData("application/vnd.example.v12.6.0+json", "12.6")]
	[InlineData("application/vnd.example.v15+json", "15")]
	[InlineData("application/vnd.example.errors.v2+json", "2")]
	[InlineData("application/vnd.example.v11.1.0+json; charset=utf-8", "11.1")]
	[InlineData("APPLICATION/VND.EXAMPLE.V3+JSON", "3")]
	[InlineData("application/vnd.example.v1", "1")]
	public void Reads_the_version_token_from_a_vendor_media_type(string mediaType, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.Read(mediaType));
	}

	[Theory]
	[InlineData("application/json")]
	[InlineData("text/plain")]
	[InlineData("application/vnd.example.vendor+json")]
	[InlineData("")]
	[InlineData(null)]
	public void Reads_no_version_from_a_media_type_without_one(string? mediaType)
	{
		Assert.Null(MediaTypeVersion.Read(mediaType));
	}

	[Theory]
	[InlineData("15.0.0", "15")]
	[InlineData("15.0", "15")]
	[InlineData("15", "15")]
	[InlineData("12.6.0", "12.6")]
	[InlineData("1.0.0", "1")]
	[InlineData("0", "0")]
	[InlineData("v7.0", "7")]
	[InlineData("01.02", "1.2")]
	public void Normalizes_by_dropping_trailing_zero_parts(string version, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.Normalize(version));
	}

	[Theory]
	[InlineData("12.6", "V12_6")]
	[InlineData("15", "V15")]
	[InlineData(null, "V0")]
	public void Names_the_version_member(string? version, string expected)
	{
		Assert.Equal(expected, MediaTypeVersion.MemberName(version));
	}
}
