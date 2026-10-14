using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioRecorderTrayApp.Converters
{
    public sealed class HumanDurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int seconds && seconds >= 0)
            {
                int s = seconds % 60;
                int totalMinutes = seconds / 60;
                int m = totalMinutes % 60;
                int totalHours = totalMinutes / 60;
                int h = totalHours % 24;
                int d = totalHours / 24;
                if (d > 0)
                {
                    return h > 0 ? $"{d}д {h}ч" : $"{d}д";
                }

                if (h > 0)
                {
                    if (m > 0 && s > 0)
                        return $"{h}ч {m}м {s}с";
                    if (m > 0)
                        return $"{h}ч {m}м";
                    if (s > 0)
                        return $"{h}ч {s}с";
                    return $"{h}ч";
                }

                if (m > 0)
                {
                    return s > 0 ? $"{m}м {s}с" : $"{m}м";
                }

                return $"{s}с";
            }

            return "0с";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
