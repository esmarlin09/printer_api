namespace LitePrintApi.Models;

public class PrinterInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Default { get; set; }
    public bool? WorkOffline { get; set; }
    public string? Status { get; set; }
}

