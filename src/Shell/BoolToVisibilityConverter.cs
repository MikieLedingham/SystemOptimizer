// File: Helpers/BoolToVisibilityConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace SystemOptimizer.Shell
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isChecked = (value as bool?) == true;
            bool invert = parameter as string == "False";
            if (invert)
                isChecked = !isChecked;
            return isChecked ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is Visibility vis && vis == Visibility.Visible);
        }
    }
}
