using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Unblind
{
	/// <summary>
	/// Base class for all view models that provides default property changed event handling
	/// </summary>
	public class ViewModelBase : INotifyPropertyChanged
	{
		/// <summary>
		/// fires an event when a property's value is changed
		/// </summary>
		public event PropertyChangedEventHandler PropertyChanged = ( sender, e ) => { };

		protected void OnPropertyChanged( [CallerMemberName] string propName = null )
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
		}

	}

}
