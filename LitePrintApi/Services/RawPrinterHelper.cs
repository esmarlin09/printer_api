using System.Runtime.InteropServices;
using System.Text;

namespace LitePrintApi.Services;

/// <summary>
/// Helper para enviar datos directamente a la impresora usando Windows API (RAW printing)
/// </summary>
public static class RawPrinterHelper
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public class DOCINFOA
    {
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDocName;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pOutputFile;
        [MarshalAs(UnmanagedType.LPStr)]
        public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool OpenPrinter([MarshalAs(UnmanagedType.LPStr)] string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterA", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true, CallingConvention = CallingConvention.StdCall)]
    public static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    /// <summary>
    /// Envía una cadena de texto directamente a la impresora (RAW)
    /// </summary>
    public static bool SendStringToPrinter(string printerName, string content, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        byte[] bytes = encoding.GetBytes(content);
        return SendBytesToPrinter(printerName, bytes);
    }

    /// <summary>
    /// Envía bytes directamente a la impresora (RAW)
    /// </summary>
    public static bool SendBytesToPrinter(string printerName, byte[] bytes)
    {
        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pBytes = IntPtr.Zero;

        try
        {
            // Abrir la impresora
            if (!OpenPrinter(printerName, out hPrinter, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"No se pudo abrir la impresora '{printerName}'. Error: {error}");
            }

            // Iniciar documento
            DOCINFOA di = new DOCINFOA
            {
                pDocName = "LitePrint Invoice",
                pDataType = "RAW"
            };

            if (!StartDocPrinter(hPrinter, 1, di))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"No se pudo iniciar el documento en la impresora. Error: {error}");
            }

            // Iniciar página
            if (!StartPagePrinter(hPrinter))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"No se pudo iniciar la página. Error: {error}");
            }

            // Asignar memoria no administrada para los bytes
            pBytes = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, pBytes, bytes.Length);

            // Escribir datos
            if (!WritePrinter(hPrinter, pBytes, bytes.Length, out int dwWritten))
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"Error al escribir en la impresora. Error: {error}");
            }

            if (dwWritten != bytes.Length)
            {
                throw new Exception($"No se escribieron todos los bytes. Escritos: {dwWritten}, Total: {bytes.Length}");
            }

            // Finalizar página
            EndPagePrinter(hPrinter);

            // Finalizar documento
            EndDocPrinter(hPrinter);

            return true;
        }
        finally
        {
            // Liberar memoria
            if (pBytes != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(pBytes);
            }

            // Cerrar impresora
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }
}

