using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

/*
 * Based on the generic boolean value converter by Anthony Jones (http://geekswithblogs.net/codingbloke/archive/2010/05/28/a-generic-boolean-value-converter.aspx)
 */

namespace Unblind
{
	public class BooleanToValueConverter<T> : IValueConverter
	{

		public T FalseValue { get; set; }
		public T TrueValue { get; set; }

		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			if(value == null)
			{
				return FalseValue;
			}

			return (bool)value ? TrueValue : FalseValue;
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			return value != null ? value.Equals(TrueValue) : false;
		}
	}

	[ValueConversion(typeof(bool), typeof(String))]
	public class BooleanToStringConverter : BooleanToValueConverter<String> { }

}
