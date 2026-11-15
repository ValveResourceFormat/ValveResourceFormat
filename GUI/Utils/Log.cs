namespace GUI.Utils;

internal static class Log
{
    public enum Category
    {
        DEBUG,
        INFO,
        WARN,
        ERROR,
    }

    private static ConsoleTab? console;
    public static void SetConsoleTab(ConsoleTab control)
    {
        console = control;
    }

    private static void WriteToConsole(string component, string message)
    {
#pragma warning disable RS0030 // Fallback before the console tab exists
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{component}] {message}");
#pragma warning restore RS0030
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void AddToAutomationLog(Category category, string component, string message)
    {
#if DEBUG
        Automation.AutomationLog.Add(category, component, message);
#endif
    }

    public static void Debug(string component, string message)
    {
        AddToAutomationLog(Category.DEBUG, component, message);

        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

#if DEBUG
#pragma warning disable RS0030 // Mirrors the console tab to the debugger output
        System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
#pragma warning restore RS0030
#endif

        console.WriteLine(Category.DEBUG, component, message);
    }

    public static void Info(string component, string message)
    {
        AddToAutomationLog(Category.INFO, component, message);

        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.INFO, component, message);
    }

    public static void Warn(string component, string message)
    {
        AddToAutomationLog(Category.WARN, component, message);

        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.WARN, component, message);
    }

    public static void Error(string component, string message)
    {
        AddToAutomationLog(Category.ERROR, component, message);

        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.ERROR, component, message);
    }

    public static void ClearConsole() => console?.ClearBuffer();
}
