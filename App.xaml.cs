using System.Windows;

namespace SelfHealingPipeline;

public partial class App : Application
{
    private bool _isDarkTheme = true;

    public void ToggleTheme()
    {
        _isDarkTheme = !_isDarkTheme;
        var dict = new ResourceDictionary
        {
            Source = new System.Uri(_isDarkTheme
                ? "Resources/Themes/DarkTheme.xaml"
                : "Resources/Themes/LightTheme.xaml", System.UriKind.Relative)
        };

        Resources.MergedDictionaries.RemoveAt(1);
        Resources.MergedDictionaries.Add(dict);
    }

    public bool IsDarkTheme => _isDarkTheme;
}
