using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Unblind
{
	[ValueConversion(typeof(double), typeof(String))]
	class TransitionDurationStringConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			//convert double to string, and divide by 60000 to convert from milliseconds into minutes
			return ((double)value / 60000.0).ToString();
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			//string to double conversion
			string text = value as string;
			double result = 0;
			if ( text == null || text.Length == 0 || !Double.TryParse(text, out result) ) return 0.0;

			//multiply by 60,000 to convert to milliseconds as well
			return result * 60000.0;
		}
	}
}
