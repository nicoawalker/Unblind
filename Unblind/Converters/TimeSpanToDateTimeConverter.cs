using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Unblind
{
	[ValueConversion(typeof(TimeSpan), typeof(DateTime))]
	class TimeSpanToDateTimeConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			TimeSpan time = (TimeSpan)value;

			if ( time == null ) return new DateTime();

			return new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, time.Hours, time.Minutes, time.Seconds, time.Milliseconds);
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			DateTime date = (DateTime)value;
			if ( date == null ) return TimeSpan.FromMilliseconds(0);

			return date.TimeOfDay;
		}
	}
}
