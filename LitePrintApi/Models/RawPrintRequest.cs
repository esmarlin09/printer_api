namespace LitePrintApi.Models;

public class RawPrintRequest
{
    public string Printer { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Encoding { get; set; } = "UTF-8";
    public bool AddCutCommand { get; set; } = false;
    public int FeedLinesCount { get; set; } = 3;
}

