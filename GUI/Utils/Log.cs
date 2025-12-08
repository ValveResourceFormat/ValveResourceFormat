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
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{component}] {message}");
    }

    public static void Debug(string component, string message)
    {
        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[{component}] {message}");
        console.WriteLine(Category.DEBUG, component, message);
    }

    public static void Info(string component, string message)
    {
        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.INFO, component, message);
    }

    public static void Warn(string component, string message)
    {
        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.WARN, component, message);
    }

    public static void Error(string component, string message)
    {
        if (console == null)
        {
            WriteToConsole(component, message);
            return;
        }

        console.WriteLine(Category.ERROR, component, message);
    }

    public static void ClearConsole() => console?.ClearBuffer();
}
