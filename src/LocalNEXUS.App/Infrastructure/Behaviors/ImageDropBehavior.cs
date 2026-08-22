using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LocalNEXUS.App.ViewModels;

namespace LocalNEXUS.App.Infrastructure.Behaviors;

/// <summary>
/// Lets an image be pasted or dropped onto the request box.
/// </summary>
/// <remarks>
/// An attached behaviour rather than code behind, which stays a call to InitializeComponent. What
/// it needs is the two things a view model cannot reach: the clipboard, and a drop.
///
/// Both are turned into bytes here and handed over. Nothing further along sees an image: the view
/// model gives the bytes to the vision model and puts the text it produced into the box, which is
/// how an image reaches a graph that only carries text.
///
/// A screenshot on the clipboard is a bitmap with no file behind it, and a dropped file is a path
/// with no bitmap, so the two arrive by different routes and are encoded to png here to give the
/// vision model one thing to expect.
/// </remarks>
public static class ImageDropBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(ImageDropBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>Extensions a dropped file is read as an image.</summary>
    private static readonly string[] ImageFiles = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box)
        {
            return;
        }

        DataObject.RemovePastingHandler(box, OnPaste);
        box.PreviewDragOver -= OnDragOver;
        box.Drop -= OnDrop;

        if (e.NewValue is not true)
        {
            return;
        }

        box.AllowDrop = true;

        DataObject.AddPastingHandler(box, OnPaste);
        box.PreviewDragOver += OnDragOver;
        box.Drop += OnDrop;
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox box || Feed(box) is not { } feed)
        {
            return;
        }

        // Text on the clipboard is text, and pasting a path from a file manager is not pasting an
        // image. Only a bitmap is taken, which is what a screenshot tool leaves.
        if (e.SourceDataObject.GetDataPresent(DataFormats.Text)
            || !Clipboard.ContainsImage())
        {
            return;
        }

        if (Clipboard.GetImage() is not { } bitmap)
        {
            return;
        }

        e.CancelCommand();
        _ = feed.AttachImageAsync(Encode(bitmap), "image/png");
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = Dropped(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not TextBox box || Feed(box) is not { } feed || Dropped(e) is not { } path)
        {
            return;
        }

        e.Handled = true;

        byte[] bytes;

        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported through the same path a failed reading is, so there is one place a person
            // looks for what happened to their image.
            _ = feed.AttachImageAsync(Array.Empty<byte>(), "image/png");
            return;
        }

        _ = feed.AttachImageAsync(bytes, MediaTypeFor(path));
    }

    /// <summary>The dropped file, when exactly one was dropped and it looks like an image.</summary>
    private static string? Dropped(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files)
        {
            return null;
        }

        var extension = Path.GetExtension(files[0]);

        return ImageFiles.Contains(extension, StringComparer.OrdinalIgnoreCase) ? files[0] : null;
    }

    private static string MediaTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "image/png"
    };

    /// <summary>A clipboard bitmap as png bytes.</summary>
    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }

    private static ActivityFeedViewModel? Feed(TextBox box)
        => box.DataContext is MainViewModel main ? main.Feed : null;
}
