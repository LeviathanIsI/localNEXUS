using System.IO;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// Writes unhandled exceptions to the logs folder.
/// </summary>
/// <remarks>
/// A dialog is easy to dismiss and impossible to quote later. Writing the same detail to a file
/// means a failure can still be diagnosed after the fact, and the dialog only has to say where
/// to look.
/// </remarks>
public static class CrashLog
{
    /// <summary>
    /// Records an exception and returns the file it was written to, or null when even logging
    /// failed, which must never itself become an error.
    /// </summary>
    public static string? Write(string context, Exception exception)
    {
        try
        {
            AppPaths.EnsureCreated();
            var path = AppPaths.CreateLogFilePath("crash");

            var content =
                $"LocalNEXUS crash report{Environment.NewLine}" +
                $"Time: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                $"Context: {context}{Environment.NewLine}{Environment.NewLine}" +
                exception;

            File.WriteAllText(path, content);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
