namespace LitePrintApi.Services;

/// <summary>
/// Comandos ESC/P para impresoras Epson (incluye LX-350)
/// </summary>
public static class EpsonEscPCommands
{
    // Comandos de control
    public const byte ESC = 0x1B;
    public const byte LF = 0x0A;
    public const byte FF = 0x0C;
    public const byte GS = 0x1D;
    
    // Inicializar impresora
    public const string INIT = "\x1B@";
    
    // Formato de texto - Negrita
    public const string BOLD_ON = "\x1BE\x01";
    public const string BOLD_OFF = "\x1BE\x00";
    
    // Subrayado
    public const string UNDERLINE_ON = "\x1B-\x01";
    public const string UNDERLINE_OFF = "\x1B-\x00";
    
    // Tamaños de fuente
    public const string FONT_NORMAL = "\x1BM";          // Fuente normal
    public const string FONT_COMPRESSED = "\x0F";       // Fuente comprimida (17 cpi)
    public const string FONT_NORMAL_EXIT = "\x12";      // Salir de fuente comprimida
    
    // Alineación
    public const string ALIGN_LEFT = "\x1Ba\x00";
    public const string ALIGN_CENTER = "\x1Ba\x01";
    public const string ALIGN_RIGHT = "\x1Ba\x02";
    
    // Espaciado de líneas
    public const string LINE_FEED = "\x0A";
    public const string FORM_FEED = "\x0C";
    
    // Avance de líneas
    public static string FeedLines(int count)
    {
        if (count <= 0) return string.Empty;
        if (count == 1) return LINE_FEED;
        return $"\x1Bd{Convert.ToChar(count)}";
    }
    
    // Corte de papel (si está disponible)
    public const string CUT_PAPER = "\x1D\x56\x00";           // Corte parcial
    public const string CUT_PAPER_FULL = "\x1D\x56\x01";      // Corte completo
    
    // Impresión de caracteres
    public const string DOUBLE_WIDTH_ON = "\x0E";
    public const string DOUBLE_WIDTH_OFF = "\x14";
    
    // Densidad/calidad
    public const string DENSITY_MEDIUM = "\x1B7";
}

