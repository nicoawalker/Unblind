using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Unblind
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{

		protected override void OnStartup( StartupEventArgs e )
		{
			base.OnStartup(e);

			this.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(_DispatcherUnhandledException);

			//prevent application from running and display an error message if the user does not meet the system requirements
			if (!VerifyNETFrameworkVersion())
			{
				MessageBoxResult result = MessageBox.Show("It appears that you don't have the required version of .NET Framework installed. Unblind requires version 4.5 or later to run.\n\nWould you like to be taken to Microsoft's website where you can download the latest version?", "Oh no!", MessageBoxButton.YesNo, MessageBoxImage.Error);

				if(result == MessageBoxResult.Yes)
				{
					System.Diagnostics.Process.Start("https://dotnet.microsoft.com/download/dotnet-framework");
				}

				this.Shutdown();
			}
		}

		private void _DispatcherUnhandledException( object sender, DispatcherUnhandledExceptionEventArgs e )
		{
			System.Windows.MessageBox.Show("An unhandled exception was caught: " + e.Exception.Message, "Oops!");
			e.Handled = true;
		}

		private static bool VerifyNETFrameworkVersion()
		{
			const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";

			using ( var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey) )
			{
				if ( ndpKey != null && ndpKey.GetValue("Release") != null )
				{
					if ( ((int)ndpKey.GetValue("Release")) < 378389 )
					{
						return false;
					}

				}else
				{
					return false;
				}
			}

			return true;
		}

	}
}
