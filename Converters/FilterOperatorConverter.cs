using System;
using System.Globalization;
using System.Windows.Data;
using MusikArchivApp.Localization;
using MusikArchivApp.Models;

namespace MusikArchivApp.Converters
{
    /// <summary>
    /// Konvertiert einen <see cref="FilterOperator"/>-Wert in den lokalisierten Anzeigetext.
    /// </summary>
    [ValueConversion(typeof(FilterOperator), typeof(string))]
    public class FilterOperatorConverter : IValueConverter
    {
        public static FilterOperatorConverter Instance { get; } = new FilterOperatorConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FilterOperator op)
            {
                var r = AppResources.Current;
                return op switch
                {
                    FilterOperator.Contains    => r.FilterOp_Contains,
                    FilterOperator.NotContains => r.FilterOp_NotContains,
                    FilterOperator.StartsWith  => r.FilterOp_StartsWith,
                    FilterOperator.EndsWith    => r.FilterOp_EndsWith,
                    FilterOperator.Equals      => r.FilterOp_Equals,
                    _                          => op.ToString()
                };
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
