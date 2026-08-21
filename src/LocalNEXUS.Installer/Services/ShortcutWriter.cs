using System.IO;

namespace LocalNEXUS.Installer.Services;

/// <summary>
/// Writes and removes the start menu and desktop shortcuts.
/// </summary>
/// <remarks>
/// Through the Windows Script Host shell object by late binding rather than a COM reference,
/// because a COM reference means an interop assembly beside the executable and this installer is
/// one file on purpose. The object has been present on every Windows since it mattered.
/// </remarks>
public static class ShortcutWriter
{
    /// <summary>Creates a shortcut, replacing one that is already there.</summary>
    public static void Write(string shortcutPath, string targetPath, string description)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell");

        if (type is null)
        {
            throw new SetupException(
                "The Windows Script Host is not available on this machine, so the shortcut could not be created. " +
                $"The application is installed and can be started from {targetPath}");
        }

        object? shell = null;
        object? shortcut = null;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

            shell = Activator.CreateInstance(type);
            shortcut = type.InvokeMember(
                "CreateShortcut",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut is null)
            {
                return;
            }

            var shortcutType = shortcut.GetType();

            Set(shortcutType, shortcut, "TargetPath", targetPath);
            Set(shortcutType, shortcut, "WorkingDirectory", Path.GetDirectoryName(targetPath) ?? string.Empty);
            Set(shortcutType, shortcut, "Description", description);
            Set(shortcutType, shortcut, "IconLocation", targetPath + ",0");

            shortcutType.InvokeMember(
                "Save",
                System.Reflection.BindingFlags.InvokeMethod,
                null,
                shortcut,
                Array.Empty<object>());
        }
        catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or UnauthorizedAccessException or IOException)
        {
            throw new SetupException(
                $"The shortcut at {shortcutPath} could not be created: {ex.Message} " +
                "The application itself is installed and works.",
                ex);
        }
        finally
        {
            Release(shortcut);
            Release(shell);
        }
    }

    /// <summary>Removes a shortcut. Silent when it is not there.</summary>
    public static void Remove(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A shortcut left behind is untidy rather than broken, and is not worth failing an
            // uninstall that otherwise succeeded.
        }
    }

    private static void Set(Type type, object instance, string property, string value)
        => type.InvokeMember(
            property,
            System.Reflection.BindingFlags.SetProperty,
            null,
            instance,
            new object[] { value });

    private static void Release(object? comObject)
    {
        if (comObject is not null && System.Runtime.InteropServices.Marshal.IsComObject(comObject))
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(comObject);
        }
    }
}
