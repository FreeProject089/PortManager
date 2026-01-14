using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PortManager
{
    public class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string state)
            {
                switch (state.ToUpper())
                {
                    case "ESTABLISHED": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                    case "LISTENING": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                    case "TIME_WAIT": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158));
                    case "CLOSE_WAIT": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0));
                    case "SYN_SENT": return new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 188, 212));
                    default: return new SolidColorBrush(System.Windows.Media.Color.FromRgb(221, 221, 221));
                }
            }
            return System.Windows.Media.Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EmptyToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
