namespace LitePrintApi.Models;

/// <summary>
/// Impresión directa ZPL (Zebra Programming Language) sin PDF, vía cola RAW de Windows.
/// </summary>
public class ZplPrintRequest
{
    public string Printer { get; set; } = string.Empty;
    public int Copies { get; set; } = 1;
    /// <summary>Texto ZPL completo, p. ej. ^XA^FDHola^FS^XZ</summary>
    public string Zpl { get; set; } = string.Empty;
}
