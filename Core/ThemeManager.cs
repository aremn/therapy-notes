using Microsoft.Win32;
using System;
using System.Linq;
using System.Windows;

namespace AudioRecorderTrayApp
{
    public enum Theme
    {
        Light,
        Dark,
        System
    }

    public static class ThemeManager
    {
        private static bool _isApplyingTheme = false;
        public static event Action<Theme>? ThemeChanged;
        public static Theme CurrentTheme { get; private set; } = Theme.Light;

        public static void ApplyTheme(Theme theme)
        {
            if (_isApplyingTheme)
            {
                System.Diagnostics.Debug.WriteLine("[ThemeManager] Предотвращена рекурсия в ApplyTheme");
                return;
            }

            _isApplyingTheme = true;
            try
            {
                var app = System.Windows.Application.Current;
                var md = app.Resources.MergedDictionaries;
                for (int i = md.Count - 1; i >= 0; i--)
                {
                    var src = md[i].Source?.ToString() ?? string.Empty;
                    if (src.EndsWith("Themes/DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("Themes/LightTheme.xaml", StringComparison.OrdinalIgnoreCase) || src.EndsWith("Styles/CommonStyles.xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        md.RemoveAt(i);
                    }
                }

                var commonStyles = new ResourceDictionary
                {
                    Source = new Uri("Styles/CommonStyles.xaml", UriKind.Relative)
                };
                md.Add(commonStyles);
                if (theme == Theme.System)
                {
                    theme = GetSystemTheme();
                }

                CurrentTheme = theme;
                ResourceDictionary themeDictionary;
                if (theme == Theme.Dark)
                {
                    themeDictionary = new ResourceDictionary
                    {
                        Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                    };
                }
                else
                {
                    themeDictionary = new ResourceDictionary
                    {
                        Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                    };
                }

                md.Add(themeDictionary);
                if (themeDictionary.Contains("PrimaryBackgroundColor"))
                    app.Resources["AppBackground"] = themeDictionary["PrimaryBackgroundColor"];
                if (themeDictionary.Contains("SecondaryBackgroundColor"))
                    app.Resources["CardBackground"] = themeDictionary["SecondaryBackgroundColor"];
                if (themeDictionary.Contains("PrimaryTextColor"))
                {
                    app.Resources["TextPrimary"] = themeDictionary["PrimaryTextColor"];
                    app.Resources["PrimaryTextColor"] = themeDictionary["PrimaryTextColor"];
                }

                if (themeDictionary.Contains("SecondaryTextColor"))
                {
                    app.Resources["TextSecondary"] = themeDictionary["SecondaryTextColor"];
                    app.Resources["SecondaryTextColor"] = themeDictionary["SecondaryTextColor"];
                }

                if (themeDictionary.Contains("AccentColorBrush"))
                {
                    app.Resources["AccentColor"] = themeDictionary["AccentColorBrush"];
                    app.Resources["AccentColorBrush"] = themeDictionary["AccentColorBrush"];
                }

                if (themeDictionary.Contains("BorderColor"))
                    app.Resources["BorderColor"] = themeDictionary["BorderColor"];
                CopyOrMutateBrush(themeDictionary, app, "ControlHoverBackgroundColor");
                CopyOrMutateBrush(themeDictionary, app, "ScrollBarThumbBrush");
                CopyOrMutateBrush(themeDictionary, app, "ScrollBarThumbHoverBrush");
                CopyOrMutateBrush(themeDictionary, app, "PrimaryAccentColorTransparent");
                CopyOrMutateBrush(themeDictionary, app, "AnalysisBodyTextBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusReadyBackgroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusReadyForegroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusReadyDotBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusRecordingBackgroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusRecordingForegroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusRecordingDotBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusProcessingBackgroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusProcessingForegroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "StatusProcessingDotBrush");
                CopyOrMutateBrush(themeDictionary, app, "ChatTherapistBackgroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "ChatClientBackgroundBrush");
                CopyOrMutateBrush(themeDictionary, app, "ChatTherapistAccentBrush");
                CopyOrMutateBrush(themeDictionary, app, "ChatClientAccentBrush");
            }
            finally
            {
                _isApplyingTheme = false;
            }

            try
            {
                ThemeChanged?.Invoke(CurrentTheme);
            }
            catch
            {
            }
        }

        private static void CopyIfPresent(ResourceDictionary from, System.Windows.Application app, string key)
        {
            if (from.Contains(key))
                app.Resources[key] = from[key];
        }

        private static void CopyOrMutateBrush(ResourceDictionary from, System.Windows.Application app, string key)
        {
            if (!from.Contains(key))
                return;
            var incoming = from[key];
            if (incoming is System.Windows.Media.SolidColorBrush incomingBrush && app.Resources.Contains(key) && app.Resources[key] is System.Windows.Media.SolidColorBrush existingBrush && !existingBrush.IsFrozen)
            {
                existingBrush.Color = incomingBrush.Color;
                existingBrush.Opacity = incomingBrush.Opacity;
            }
            else
            {
                app.Resources[key] = incoming;
            }
        }

        private static Theme GetSystemTheme()
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null && key.GetValue("AppsUseLightTheme")is int value)
                {
                    return value == 1 ? Theme.Light : Theme.Dark;
                }
            }
            catch
            {
            }

            return Theme.Light;
        }
    }
}
