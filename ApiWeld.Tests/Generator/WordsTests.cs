using ApiWeld.Generator;

namespace ApiWeld.Tests.Generator;

public class WordsTests
{
	[Theory]
	[InlineData("person-communication-codes", "person|communication|codes")]
	[InlineData("studentId", "student|Id")]
	[InlineData("errors_2_0_0", "errors|2|0|0")]
	[InlineData("XMLHttpRequest", "XML|Http|Request")]
	[InlineData("qapi", "qapi")]
	public void Splits_text_into_words(string text, string expected)
	{
		Assert.Equal(expected, string.Join('|', Words.Split(text)));
	}

	[Theory]
	[InlineData("sections-seats", "SectionsSeats")]
	[InlineData("academic-levels", "AcademicLevels")]
	[InlineData("errors_2_0_0", "Errors200")]
	[InlineData("x-total-count", "XTotalCount")]
	[InlineData("", "")]
	public void Pascal_cases_text(string text, string expected)
	{
		Assert.Equal(expected, Words.Pascal(text));
	}

	[Theory]
	[InlineData("persons", "Person")]
	[InlineData("addresses", "Address")]
	[InlineData("statuses", "Status")]
	[InlineData("academic-disciplines", "AcademicDiscipline")]
	[InlineData("categories", "Category")]
	[InlineData("boxes", "Box")]
	[InlineData("courses", "Course")]
	[InlineData("status", "Status")]
	[InlineData("analysis", "Analysis")]
	[InlineData("class", "Class")]
	[InlineData("seats", "Seat")]
	[InlineData("person-communication-codes", "PersonCommunicationCode")]
	public void Makes_the_last_word_singular(string text, string expected)
	{
		Assert.Equal(expected, Words.SingularPascal(text));
	}

	[Theory]
	[InlineData("2fa", "_2fa")]
	[InlineData("class", "@class")]
	[InlineData("", "Value")]
	[InlineData("Name", "Name")]
	public void Makes_a_valid_identifier(string candidate, string expected)
	{
		Assert.Equal(expected, Words.Identifier(candidate));
	}

	[Theory]
	[InlineData("StudentId", "studentId")]
	[InlineData("student-id", "studentId")]
	public void Camel_cases_text(string text, string expected)
	{
		Assert.Equal(expected, Words.Camel(text));
	}

	[Fact]
	public void Numbers_a_name_until_it_is_unique()
	{
		var taken = new HashSet<string> { "Name" };

		Assert.Equal("Name2", Words.Unique("Name", taken));
		Assert.Equal("Name3", Words.Unique("Name", taken));
		Assert.Equal("Other", Words.Unique("Other", taken));
	}
}
