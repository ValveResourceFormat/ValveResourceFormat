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

    private static ConsoleTab console;
    public static void SetConsoleTab(ConsoleTab control)
    {
        console = control;
    }

    public static void Debug(string component, string message) => console.WriteLine(Category.DEBUG, component, message);
    public static void Info(string component, string message) => console.WriteLine(Category.INFO, component, message);
    public static void Warn(string component, string message) => console.WriteLine(Category.WARN, component, message);
    public static void Error(string component, string message) => console.WriteLine(Category.ERROR, component, message);
    public static void ClearConsole() => console.ClearBuffer();
}
