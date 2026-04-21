using System.Runtime.InteropServices;

namespace LitePrintApi.Services;

/// <summary>Envía bytes en modo RAW a una impresora Windows (p. ej. ZPL a Zebra).</summary>
internal static class WindowsRawPrint
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pDocName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string? pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [MarshalAs(UnmanagedType.LPStruct)] DocInfo docInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    public static void SendBytes(string printerName, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            throw new ArgumentException("No hay datos para enviar a la impresora.");

        if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero))
            throw new InvalidOperationException($"OpenPrinter falló para '{printerName}' (Win32 {Marshal.GetLastWin32Error()}).");

        try
        {
            var doc = new DocInfo
            {
                pDocName = "LitePrint ZPL",
                pOutputFile = null,
                pDataType = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, doc))
                throw new InvalidOperationException($"StartDocPrinter falló (Win32 {Marshal.GetLastWin32Error()}).");

            try
            {
                if (!StartPagePrinter(hPrinter))
                    throw new InvalidOperationException($"StartPagePrinter falló (Win32 {Marshal.GetLastWin32Error()}).");

                try
                {
                    byte[] buffer = data.ToArray();
                    IntPtr unmanaged = Marshal.AllocCoTaskMem(buffer.Length);
                    try
                    {
                        Marshal.Copy(buffer, 0, unmanaged, buffer.Length);

                        if (!WritePrinter(hPrinter, unmanaged, buffer.Length, out int written))
                            throw new InvalidOperationException($"WritePrinter falló (Win32 {Marshal.GetLastWin32Error()}).");
                        if (written != buffer.Length)
                            throw new InvalidOperationException($"WritePrinter: se enviaron {written} de {buffer.Length} bytes.");
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(unmanaged);
                    }
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        finally
        {
            ClosePrinter(hPrinter);
        }
    }
}
