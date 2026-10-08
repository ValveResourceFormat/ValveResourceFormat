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

    public static void Debug(string component, string message) => Write(Category.DEBUG, component, message);

    public static void Info(string component, string message) => Write(Category.INFO, component, message);

    public static void Warn(string component, string message) => Write(Category.WARN, component, message);

    public static void Error(string component, string message) => Write(Category.ERROR, component, message);

    private static void Write(Category category, string component, string message)
    {
#if DEBUG
        Automation.AutomationLog.Add(category, component, message);
#endif

        if (console == null)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{component}] {message}");
            return;
        }

#if DEBUG
        if (category == Category.DEBUG)
        {
            System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
        }
#endif

        console.WriteLine(category, component, message);
    }

    public static void ClearConsole() => console?.ClearBuffer();
}
