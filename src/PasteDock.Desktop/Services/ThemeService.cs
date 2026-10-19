using System.Windows;

namespace PasteDock.Desktop.Services;

internal static class ThemeService
{
    public static void Apply(string theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary
        { Source = new Uri($"Themes/Tokens.{(theme == "Dark" ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        // Keep the token dictionary in front of the shared control dictionary.
        dictionaries[0] = next;
    }
}
