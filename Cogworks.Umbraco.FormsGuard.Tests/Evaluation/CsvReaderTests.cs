using Cogworks.Umbraco.FormsGuard.Evaluation;

namespace Cogworks.Umbraco.FormsGuard.Tests.Evaluation;

public class CsvReaderTests
{
    private const string Header = "\"label\",\"category\",\"name\",\"email\",\"message\"\n";

    [Fact]
    public void Quoted_field_with_commas_and_doubled_quotes_is_one_field()
    {
        var rows = CsvReader.ReadCorpus(Header + "\"spam\",\"obvious\",\"A, B\",\"a@b.example\",\"Say \"\"hi\"\", then, go\"\n");

        var row = Assert.Single(rows);
        Assert.Equal("A, B", row.Name);
        Assert.Equal("Say \"hi\", then, go", row.Message);
        Assert.True(row.IsSpam);
    }

    [Fact]
    public void Columns_are_mapped_by_header_and_crlf_and_blank_lines_are_handled()
    {
        var text = "message,email,name,category,label\r\nhello,x@y.example,Sam,legitimate,genuine\r\n\r\n";

        var row = Assert.Single(CsvReader.ReadCorpus(text));
        Assert.Equal("hello", row.Message);
        Assert.Equal("Sam", row.Name);
        Assert.Equal("genuine", row.Label);
        Assert.False(row.IsSpam);
        Assert.Equal(2, row.Line);
    }

    [Fact]
    public void Unterminated_quote_throws()
    {
        Assert.Throws<FormatException>(() => CsvReader.ReadCorpus(Header + "\"spam\",\"obvious\",\"A\",\"a@b\",\"oops\n"));
    }

    [Fact]
    public void Unknown_label_throws_naming_the_line()
    {
        var ex = Assert.Throws<FormatException>(() => CsvReader.ReadCorpus(Header + "\"spm\",\"obvious\",\"A\",\"a@b\",\"m\"\n"));
        Assert.Contains("Line 2", ex.Message);
    }

    [Fact]
    public void Missing_header_column_throws()
    {
        Assert.Throws<FormatException>(() => CsvReader.ReadCorpus("label,name,email,message\nspam,A,a@b,m\n"));
    }

    [Fact]
    public void Short_row_throws()
    {
        Assert.Throws<FormatException>(() => CsvReader.ReadCorpus(Header + "spam,obvious,A\n"));
    }

    [Fact]
    public void Shipped_corpus_has_nine_labelled_rows()
    {
        var path = Path.Combine(FindRepoRoot(), "demo", "spam-corpus.csv");
        var rows = CsvReader.ReadCorpus(File.ReadAllText(path));

        Assert.Equal(9, rows.Count);
        Assert.Equal(6, rows.Count(r => r.IsSpam));
        Assert.Equal(3, rows.Count(r => r.Label == "genuine"));
        Assert.Contains(rows, r => r.Message.Contains("Hello, I noticed your site", StringComparison.Ordinal));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cogworks.Umbraco.FormsGuard.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
