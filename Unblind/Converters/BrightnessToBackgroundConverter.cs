using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Unblind
{
	[ValueConversion(typeof(uint), typeof(String))]
	public class BrightnessToBackgroundConverter : IValueConverter
	{
		public object Convert( object value, Type targetType, object parameter, CultureInfo culture )
		{
			if(value == null) return "Images\\Bg25.bmp";

			uint brightness = (uint)value;

			switch( brightness )
			{
				case uint n when brightness <= 25:
					return "Images\\Bg25.bmp";
				case uint n when brightness > 25 && brightness <= 50:
					return "Images\\Bg50.bmp";
				case uint n when brightness > 50 && brightness <= 75:
					return "Images\\Bg75.bmp";
				case uint n when brightness > 75:
					return "Images\\Bg100.bmp";
				default: return "Images\\Bg25.bmp";
			}
		}

		public object ConvertBack( object value, Type targetType, object parameter, CultureInfo culture )
		{
			throw new NotImplementedException();
		}
	}
}
