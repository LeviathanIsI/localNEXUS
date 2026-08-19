using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Nodes;

/// <summary>One literal substitution applied by a transform node in template mode.</summary>
public sealed partial class FindReplacePair : ObservableObject
{
    /// <summary>The literal text to look for. An empty value disables the pair.</summary>
    [ObservableProperty]
    private string _find = string.Empty;

    /// <summary>The text to substitute in its place.</summary>
    [ObservableProperty]
    private string _replace = string.Empty;

    public FindReplacePair()
    {
    }

    public FindReplacePair(string find, string replace)
    {
        Find = find;
        Replace = replace;
    }
}
