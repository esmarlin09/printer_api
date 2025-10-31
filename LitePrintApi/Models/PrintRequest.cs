namespace LitePrintApi.Models;

public class PrintRequest
{
    public string Printer { get; set; } = string.Empty;
    public int Copies { get; set; } = 1;
    public string Base64Pdf { get; set; } = string.Empty;
    public bool RemoveMargins { get; set; } = false;
}

