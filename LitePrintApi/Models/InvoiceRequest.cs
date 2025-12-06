namespace LitePrintApi.Models;

public class InvoiceRequest
{
    public string Printer { get; set; } = string.Empty;
    
    // Información de la empresa (vendedor)
    public CompanyInfo Company { get; set; } = new();
    
    // Información del cliente (comprador)
    public CustomerInfo Customer { get; set; } = new();
    
    // Datos de la factura
    public InvoiceInfo Invoice { get; set; } = new();
    
    // Items de la factura
    public List<InvoiceItem> Items { get; set; } = new();
    
    // Totales
    public InvoiceTotals Totals { get; set; } = new();
    
    // Nota legal
    public string? LegalNote { get; set; }
    
    // Opciones de impresión
    public bool AddCutCommand { get; set; } = true;
    public int FeedLinesCount { get; set; } = 3;
}

public class CompanyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Rnc { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
}

public class CustomerInfo
{
    public string Name { get; set; } = string.Empty;
    public string? Rnc { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
}

public class InvoiceInfo
{
    public DateTime SaleDate { get; set; }
    public string? InvoicedBy { get; set; }
    public DateTime? DueDate { get; set; }
    public string? FiscalCreditInvoice { get; set; } // NCF
    public DateTime? NcfExpirationDate { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
}

public class InvoiceItem
{
    public decimal Quantity { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal SubTotal { get; set; }
}

public class InvoiceTotals
{
    public decimal SubTotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Itbis { get; set; }
    public decimal Total { get; set; }
}

