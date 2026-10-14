using System.Windows;

namespace AudioRecorderTrayApp
{
    public static class CalendarExtensions
    {
        public static readonly DependencyProperty HasSessionProperty = DependencyProperty.RegisterAttached("HasSession", typeof(bool), typeof(CalendarExtensions), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        public static void SetHasSession(DependencyObject element, bool value)
        {
            element.SetValue(HasSessionProperty, value);
        }

        public static bool GetHasSession(DependencyObject element)
        {
            return (bool)element.GetValue(HasSessionProperty);
        }
    }
}
