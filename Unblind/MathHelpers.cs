using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unblind
{
	public static class MathHelpers
	{

		public static T ClampValue<T>(T value, T min, T max) where T : IComparable<T>
		{
			return ( value.CompareTo(min) < 0) ? min : ( value.CompareTo(max) > 0 ) ? max : value;
		}

		/// <summary>
		/// returns the greater of two values
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="first"></param>
		/// <param name="second"></param>
		/// <returns>first, if first is greater than or equal to second, otherwise second</returns>
		public static T Greater<T>(T first, T second) where T : IComparable<T>
		{
			return (first.CompareTo(second) >= 0) ? first : second;
		}

		public static double DegreesToRadians( double degrees )
		{
			return Math.PI * degrees / 180.0;
		}

		public static double RadiansToDegrees( double radians )
		{
			return radians * 180.0 / Math.PI;
		}

	}
}
