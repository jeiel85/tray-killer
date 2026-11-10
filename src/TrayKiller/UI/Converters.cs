using System;
using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TrayKiller.UI
{
    public sealed class BoolToVis : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            (value is bool b && b) ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Visible when the value is null / empty (Invert: when it has a value).</summary>
    public sealed class NullToVis : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool empty = value == null || (value is string s && s.Length == 0);
            return empty ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>Visible when a number / collection is non-zero (Invert: when zero).</summary>
    public sealed class CountToVis : IValueConverter
    {
        public bool Invert { get; set; }
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            long n = 0;
            if (value is int i) n = i;
            else if (value is long l) n = l;
            else if (value is ICollection c) n = c.Count;
            return (n > 0) ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }

    /// <summary>For segmented radio buttons: checked when value equals the parameter.</summary>
    public sealed class EqualsToBool : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture), System.Convert.ToString(parameter, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is bool b) || !b) return Binding.DoNothing;
            if (targetType == typeof(int)) return int.Parse(System.Convert.ToString(parameter, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return parameter;
        }
    }
}
