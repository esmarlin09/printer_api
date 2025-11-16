using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace LitePrintApi.Services;

/// <summary>
/// Helper para ejecutar procesos en la sesión del usuario interactivo desde un servicio de Windows
/// </summary>
public static class InteractiveProcessHelper
{
    private const int WTS_CURRENT_SESSION = -1;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint MAXIMUM_ALLOWED = 0x2000000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const int STARTF_USESHOWWINDOW = 0x00000001;
    private const short SW_SHOW = 5;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int ImpersonationLevel,
        int TokenType,
        out IntPtr phNewToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    /// <summary>
    /// Ejecuta un proceso en la sesión del usuario interactivo
    /// </summary>
    public static Process? StartProcessInInteractiveSession(
        string executablePath,
        string arguments,
        ILogger? logger = null,
        bool showWindow = false)
    {
        try
        {
            // Obtener la sesión activa del usuario interactivo
            uint sessionId = WTSGetActiveConsoleSessionId();
            logger?.LogInformation("Sesión activa del usuario: {SessionId}", sessionId);

            if (sessionId == 0xFFFFFFFF) // INVALID_SESSION_ID
            {
                logger?.LogWarning("No se pudo obtener la sesión activa, usando sesión actual");
                // Fallback: ejecutar normalmente
                return Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = !showWindow,
                    WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                });
            }

            // Obtener el token del usuario de la sesión activa
            if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
            {
                int error = Marshal.GetLastWin32Error();
                logger?.LogWarning("No se pudo obtener el token del usuario (Error: {Error}). Intentando método alternativo.", error);
                // Fallback: usar UseShellExecute
                return Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = !showWindow,
                    WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                });
            }

            try
            {
                // Duplicar el token para poder crear el proceso
                if (!DuplicateTokenEx(
                    userToken,
                    TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_IMPERSONATE,
                    IntPtr.Zero,
                    2, // SecurityImpersonation
                    1, // TokenPrimary
                    out IntPtr duplicatedToken))
                {
                    int error = Marshal.GetLastWin32Error();
                    logger?.LogWarning("No se pudo duplicar el token (Error: {Error}). Intentando método alternativo.", error);
                    CloseHandle(userToken);
                    // Fallback
                    return Process.Start(new ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = arguments,
                        UseShellExecute = true,
                        CreateNoWindow = !showWindow,
                        WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                    });
                }

                try
                {
                    // Crear el proceso en la sesión del usuario
                    STARTUPINFO si = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf(typeof(STARTUPINFO)),
                        dwFlags = showWindow ? STARTF_USESHOWWINDOW : 0,
                        wShowWindow = showWindow ? SW_SHOW : (short)0,
                        lpDesktop = "WinSta0\\Default" // Sesión interactiva
                    };

                    string commandLine = $"\"{executablePath}\" {arguments}";
                    string currentDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    if (!CreateProcessAsUser(
                        duplicatedToken,
                        string.Empty, // Application name
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT | (showWindow ? 0 : CREATE_NO_WINDOW),
                        IntPtr.Zero,
                        currentDirectory,
                        ref si,
                        out PROCESS_INFORMATION pi))
                    {
                        int error = Marshal.GetLastWin32Error();
                        logger?.LogError("No se pudo crear el proceso (Error: {Error})", error);
                        return null;
                    }

                    logger?.LogInformation("Proceso creado exitosamente en sesión del usuario (PID: {ProcessId})", pi.dwProcessId);

                    // Cerrar handles del proceso (el proceso ya fue creado)
                    if (pi.hProcess != IntPtr.Zero) CloseHandle(pi.hProcess);
                    if (pi.hThread != IntPtr.Zero) CloseHandle(pi.hThread);

                    // Retornar un objeto Process usando el PID
                    return Process.GetProcessById(pi.dwProcessId);
                }
                finally
                {
                    CloseHandle(duplicatedToken);
                }
            }
            finally
            {
                CloseHandle(userToken);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Error al ejecutar proceso en sesión interactiva: {Error}", ex.Message);
            // Fallback: intentar ejecutar normalmente
            try
            {
                return Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = !showWindow,
                    WindowStyle = showWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                });
            }
            catch
            {
                return null;
            }
        }
    }
}

