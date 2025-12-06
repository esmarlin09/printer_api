using LitePrintApi.Models;

namespace LitePrintApi.Services;

/// <summary>
/// Formatea una factura para impresión en Epson LX-350 replicando el diseño de la factura física
/// </summary>
public class InvoiceFormatter
{
    private const int LINE_WIDTH = 42; // Ancho máximo para papel de 9.5" con fuente normal
    
    public string FormatInvoice(InvoiceRequest request)
    {
        var sb = new System.Text.StringBuilder();
        
        // Inicializar impresora
        sb.Append(EpsonEscPCommands.INIT);
        sb.Append(EpsonEscPCommands.FONT_NORMAL);
        sb.Append(EpsonEscPCommands.ALIGN_LEFT);
        
        // ========== HEADER: Información de la empresa ==========
        sb.Append(EpsonEscPCommands.BOLD_ON);
        sb.Append(CenterText(request.Company.Name, LINE_WIDTH));
        sb.Append(EpsonEscPCommands.BOLD_OFF);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        if (!string.IsNullOrEmpty(request.Company.Rnc))
        {
            sb.Append($"R.N.C.: {request.Company.Rnc}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Company.Address))
        {
            sb.Append(request.Company.Address);
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Company.City) || !string.IsNullOrEmpty(request.Company.Country))
        {
            var cityCountry = string.Join(", ", new[] { request.Company.City, request.Company.Country }.Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(cityCountry))
            {
                sb.Append(cityCountry);
                sb.Append(EpsonEscPCommands.LINE_FEED);
            }
        }
        
        if (!string.IsNullOrEmpty(request.Company.Phone))
        {
            sb.Append($"Telefono: {request.Company.Phone}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Company.Email))
        {
            sb.Append($"Email: {request.Company.Email}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // ========== DATOS DE VENTA ==========
        sb.Append(new string('=', LINE_WIDTH));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        if (request.Invoice.SaleDate != default)
        {
            sb.Append($"FECHA DE VENTA: {request.Invoice.SaleDate:dd/MM/yyyy}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Invoice.InvoicedBy))
        {
            sb.Append($"FACTURADO POR: {request.Invoice.InvoicedBy}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (request.Invoice.DueDate.HasValue)
        {
            sb.Append($"FACTURA VENCE EL DIA: {request.Invoice.DueDate.Value:dd/MM/yyyy}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Invoice.FiscalCreditInvoice))
        {
            sb.Append($"FACTURA DE CREDITO FISCAL: {request.Invoice.FiscalCreditInvoice}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (request.Invoice.NcfExpirationDate.HasValue)
        {
            sb.Append($"FECHA VENCIMIENTO NCF: {request.Invoice.NcfExpirationDate.Value:dd/MM/yyyy}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Invoice.InvoiceNumber))
        {
            sb.Append($"FACT. No.: {request.Invoice.InvoiceNumber}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // ========== INFORMACIÓN DEL CLIENTE ==========
        if (!string.IsNullOrEmpty(request.Customer.Name))
        {
            sb.Append(EpsonEscPCommands.BOLD_ON);
            sb.Append(request.Customer.Name);
            sb.Append(EpsonEscPCommands.BOLD_OFF);
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Customer.Rnc))
        {
            sb.Append($"R.N.C.: {request.Customer.Rnc}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Customer.Phone))
        {
            sb.Append($"Telefono: {request.Customer.Phone}");
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        if (!string.IsNullOrEmpty(request.Customer.Address))
        {
            sb.Append(request.Customer.Address);
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        sb.Append(EpsonEscPCommands.LINE_FEED);
        sb.Append(new string('-', LINE_WIDTH));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // ========== TABLA DE ITEMS ==========
        // Encabezado de la tabla (similar a la factura de la imagen)
        // Formato: Cantidad | Descripción | PRECIO | Sub-Total
        sb.Append(EpsonEscPCommands.BOLD_ON);
        sb.Append("Cantidad".PadRight(10));
        sb.Append("Descripcion".PadRight(18));
        sb.Append("PRECIO".PadLeft(8));
        sb.Append("Sub-Total".PadLeft(10));
        sb.Append(EpsonEscPCommands.BOLD_OFF);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        sb.Append(new string('-', LINE_WIDTH));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // Items
        foreach (var item in request.Items)
        {
            // Cantidad (formato: 300.00)
            sb.Append(item.Quantity.ToString("N2").PadRight(10));
            
            // Descripción (puede ocupar múltiples líneas si es muy larga)
            var description = item.Description;
            var maxDescWidth = 18;
            
            if (description.Length > maxDescWidth)
            {
                // Dividir descripción en múltiples líneas
                var descLines = SplitText(description, maxDescWidth);
                
                // Primera línea: Cantidad + Descripción (sin precio aún)
                sb.Append(descLines[0].PadRight(maxDescWidth));
                sb.Append(string.Empty.PadLeft(8)); // Espacio para precio
                sb.Append(string.Empty.PadLeft(10)); // Espacio para subtotal
                sb.Append(EpsonEscPCommands.LINE_FEED);
                
                // Líneas adicionales de descripción (solo descripción, sin cantidad ni precios)
                for (int i = 1; i < descLines.Count; i++)
                {
                    sb.Append(string.Empty.PadRight(10)); // Espacio cantidad
                    sb.Append(descLines[i].PadRight(maxDescWidth));
                    sb.Append(string.Empty.PadLeft(8));
                    sb.Append(string.Empty.PadLeft(10));
                    sb.Append(EpsonEscPCommands.LINE_FEED);
                }
                
                // Última línea: Solo precio y subtotal (alineados a la derecha)
                sb.Append(string.Empty.PadRight(10)); // Espacio cantidad
                sb.Append(string.Empty.PadRight(maxDescWidth)); // Espacio descripción
                sb.Append($"${item.UnitPrice:N2}".PadLeft(8));
                sb.Append($"${item.SubTotal:N2}".PadLeft(10));
            }
            else
            {
                // Todo en una línea
                sb.Append(description.PadRight(maxDescWidth));
                sb.Append($"${item.UnitPrice:N2}".PadLeft(8));
                sb.Append($"${item.SubTotal:N2}".PadLeft(10));
            }
            
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        sb.Append(new string('-', LINE_WIDTH));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // ========== TOTALES ==========
        // Formato similar a la factura: SUB-TOTAL, DESCUENTO, ITBIS, TOTAL A PAGAR
        sb.Append("SUB-TOTAL:".PadRight(25));
        sb.Append($"${request.Totals.SubTotal:N2}".PadLeft(17));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        if (request.Totals.Discount > 0)
        {
            sb.Append("DESCUENTO:".PadRight(25));
            sb.Append($"${request.Totals.Discount:N2}".PadLeft(17));
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        sb.Append("ITBIS:".PadRight(25));
        sb.Append($"${request.Totals.Itbis:N2}".PadLeft(17));
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        sb.Append(EpsonEscPCommands.BOLD_ON);
        sb.Append("TOTAL A PAGAR:".PadRight(25));
        sb.Append($"${request.Totals.Total:N2}".PadLeft(17));
        sb.Append(EpsonEscPCommands.BOLD_OFF);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        sb.Append(EpsonEscPCommands.LINE_FEED);
        
        // ========== NOTA LEGAL ==========
        if (!string.IsNullOrEmpty(request.LegalNote))
        {
            sb.Append(EpsonEscPCommands.FONT_COMPRESSED);
            sb.Append("Nota:");
            sb.Append(EpsonEscPCommands.LINE_FEED);
            sb.Append(WrapText(request.LegalNote, LINE_WIDTH));
            sb.Append(EpsonEscPCommands.FONT_NORMAL_EXIT);
            sb.Append(EpsonEscPCommands.LINE_FEED);
        }
        
        // Avance de líneas final
        sb.Append(EpsonEscPCommands.FeedLines(request.FeedLinesCount));
        
        // Corte de papel si está configurado
        if (request.AddCutCommand)
        {
            sb.Append(EpsonEscPCommands.CUT_PAPER);
        }
        
        return sb.ToString();
    }
    
    private string CenterText(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length >= width) return text;
        
        int padding = (width - text.Length) / 2;
        return text.PadLeft(padding + text.Length).PadRight(width);
    }
    
    private List<string> SplitText(string text, int maxLength)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        
        var words = text.Split(' ');
        var currentLine = new System.Text.StringBuilder();
        
        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 <= maxLength)
            {
                if (currentLine.Length > 0) currentLine.Append(' ');
                currentLine.Append(word);
            }
            else
            {
                if (currentLine.Length > 0)
                {
                    lines.Add(currentLine.ToString());
                    currentLine.Clear();
                }
                
                // Si la palabra es más larga que maxLength, cortarla
                if (word.Length > maxLength)
                {
                    int start = 0;
                    while (start < word.Length)
                    {
                        int length = Math.Min(maxLength, word.Length - start);
                        lines.Add(word.Substring(start, length));
                        start += length;
                    }
                }
                else
                {
                    currentLine.Append(word);
                }
            }
        }
        
        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }
        
        return lines.Count > 0 ? lines : new List<string> { text };
    }
    
    private string WrapText(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        var lines = SplitText(text, maxWidth);
        return string.Join(EpsonEscPCommands.LINE_FEED, lines);
    }
}

