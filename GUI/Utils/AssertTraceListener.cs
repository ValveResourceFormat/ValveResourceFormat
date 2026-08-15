using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace GUI.Utils
{
    /// <summary>
    /// Shows failed <see cref="Debug.Assert(bool)"/> calls in the error dialog before
    /// fail-fasting the process when no debugger is attached.
    /// </summary>
    internal sealed class AssertTraceListener : DefaultTraceListener
    {
        internal sealed class AssertFailedException : Exception
        {
            public AssertFailedException() { }
            public AssertFailedException(string message) : base(message) { }
            public AssertFailedException(string message, Exception innerException) : base(message, innerException) { }
        }

        public override void Fail(string? message, string? detailMessage)
        {
            if (Debugger.IsAttached)
            {
                Debugger.Break();
                return;
            }

            var text = string.IsNullOrEmpty(message) ? "Assertion failed" : message;

            if (!string.IsNullOrEmpty(detailMessage))
            {
                text += Environment.NewLine + detailMessage;
            }

            var exception = new AssertFailedException(text);
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, new StackTrace(fNeedFileInfo: true).ToString());

            try
            {
                Program.ShowError(exception);
            }
            catch
            {
                // UI may not be available yet; fail fast regardless
            }

            Environment.FailFast(text, exception);
        }
    }
}
