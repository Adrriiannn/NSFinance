using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using NSFinance.Api.Modules.Imports.Services;
using NSFinance.Api.Modules.Imports.Validators;

namespace NSFinance.Api.Tests.Unit;

public sealed class StatementImportFormParserTests
{
    [Fact]
    public void PreviewForm_AcceptsOnlyExplicitClientMappingFields()
    {
        var accountId = Guid.NewGuid();
        var form = CreateForm(new Dictionary<string, StringValues>
        {
            ["accountId"] = accountId.ToString(),
            ["dateColumn"] = "0",
            ["descriptionColumn"] = "1",
            ["amountColumn"] = "2",
            ["dateFormat"] = "dd/MM/yyyy",
            ["locale"] = "en-IE",
            ["timeZoneId"] = "Europe/Dublin"
        });

        var error = StatementImportFormParser.TryCreatePreviewRequest(form, out var request);

        Assert.Null(error);
        Assert.NotNull(request);
        Assert.Equal(accountId, request.AccountId);
        Assert.Equal(0, request.DateColumn);
        Assert.Equal(2, request.AmountColumn);
        Assert.Null(request.DebitColumn);
    }

    [Theory]
    [InlineData("sourceFingerprint")]
    [InlineData("mappingFingerprint")]
    [InlineData("rows")]
    [InlineData("duplicateClassification")]
    public void PreviewForm_RejectsServerOwnedOrUnexpectedFields(string field)
    {
        var form = CreateForm(new Dictionary<string, StringValues>
        {
            ["accountId"] = Guid.NewGuid().ToString(),
            ["dateColumn"] = "0",
            ["descriptionColumn"] = "1",
            [field] = "client-value"
        });

        var error = StatementImportFormParser.TryCreatePreviewRequest(form, out var request);

        Assert.Null(request);
        Assert.Equal("statement_import_form_field_invalid", error!.Code);
    }

    [Fact]
    public void PreviewForm_RejectsRepeatedAndNegativeColumnValues()
    {
        var repeated = CreateForm(new Dictionary<string, StringValues>
        {
            ["accountId"] = Guid.NewGuid().ToString(),
            ["dateColumn"] = new StringValues(["0", "1"]),
            ["descriptionColumn"] = "1"
        });
        var negative = CreateForm(new Dictionary<string, StringValues>
        {
            ["accountId"] = Guid.NewGuid().ToString(),
            ["dateColumn"] = "0",
            ["descriptionColumn"] = "-1"
        });

        var repeatedError = StatementImportFormParser.TryCreatePreviewRequest(repeated, out _);
        var negativeError = StatementImportFormParser.TryCreatePreviewRequest(negative, out _);

        Assert.Equal("statement_import_form_field_repeated", repeatedError!.Code);
        Assert.Equal("statement_import_mapping_column_invalid", negativeError!.Code);
    }

    [Fact]
    public void UploadPolicy_RequiresOneBoundedCsvWithSupportedContent()
    {
        var valid = CreateFile("statement.csv", "text/csv", length: 12);
        var wrongExtension = CreateFile("statement.pdf", "text/csv", length: 12);
        var wrongContent = CreateFile("statement.csv", "application/pdf", length: 12);
        var oversized = CreateFile(
            "statement.csv",
            "text/csv",
            StatementImportStagingPolicy.MaximumFileSizeBytes + 1);

        Assert.Null(StatementImportUploadPolicy.ValidateFile(valid));
        Assert.Equal(
            "statement_csv_file_type_invalid",
            StatementImportUploadPolicy.ValidateFile(wrongExtension)!.Code);
        Assert.Equal(
            "statement_csv_file_type_invalid",
            StatementImportUploadPolicy.ValidateFile(wrongContent)!.Code);
        Assert.Equal(
            "statement_csv_file_size_invalid",
            StatementImportUploadPolicy.ValidateFile(oversized)!.Code);
    }

    [Theory]
    [InlineData(null, ",")]
    [InlineData("semicolon", ";")]
    [InlineData("tab", "\t")]
    [InlineData("pipe", "|")]
    public void UploadPolicy_NormalizesSupportedDelimiters(string? input, string expected)
    {
        var valid = StatementImportUploadPolicy.TryNormalizeDelimiter(
            input,
            out var delimiter,
            out var error);

        Assert.True(valid);
        Assert.Null(error);
        Assert.Equal(expected, delimiter);
    }

    private static FormCollection CreateForm(Dictionary<string, StringValues> values) =>
        new(values, new FormFileCollection());

    private static FormFile CreateFile(string fileName, string contentType, long length) =>
        new(new MemoryStream(new byte[] { 1 }), 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
}
