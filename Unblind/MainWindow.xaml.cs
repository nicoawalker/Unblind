using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.IO;
using System.Net;

namespace Unblind
{

	public delegate void DisplayChange();

	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{ 
		private System.Windows.Forms.NotifyIcon m_trayIcon;

		private bool m_close;

		public event DisplayChange OnDisplayChangeDetected;

		public MainWindow()
		{
			InitializeComponent();

			//override the default tooltip timeout to make tooltips stay open until the user moves the mouse off the control
			ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(Int32.MaxValue));

			m_close = true;

			MainWindowViewModel vm = null;

			try
			{
				vm = new MainWindowViewModel(this);
				this.DataContext = vm;
			}
			catch ( Win32Exception e )
			{
				MessageBox.Show("Something went wrong while attempting to connect to your displays and Unblind cannot continue to run.\n\nError message: " + e.Message, "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);
				this.Close();

			}
			catch ( AggregateException ae )
			{
				if ( ae.Flatten().InnerExceptions.Count > 1 )
				{
					string exceptionMessages = "";
					foreach ( var e in ae.Flatten().InnerExceptions )
					{
						exceptionMessages += e.Message;
						exceptionMessages += "\n";
					}

					MessageBox.Show("Multiple errors occurred during startup and Unblind cannot continue to run.\n\nThe errors that occurred were:\n" + exceptionMessages, "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);

				} else
				{
					MessageBox.Show("Something went wrong and Unblind cannot continue to run.\n\nError message: " + ae.InnerException.Message, "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);
				}

				this.Close();
			}
			catch ( Exception e )
			{
				MessageBox.Show("Something went wrong and Unblind cannot continue to run.\n\nError message: " + e.Message, "Oops!", MessageBoxButton.OK, MessageBoxImage.Error);
				this.Close();
			}

			m_close = false;

			_CreateTrayIcon();
		}

		private void _Minimize()
		{
			this.Hide();
			this.ShowInTaskbar = false;
		}

		private void _CreateTrayIcon()
		{
			//create new NotifyIcon that will function as the tray icon
			m_trayIcon = new System.Windows.Forms.NotifyIcon { Icon = Properties.Resources.UnblindTrayIcon, Visible = true };
			m_trayIcon.DoubleClick += ( object sender, EventArgs args ) =>
			{
				this.Show();
				this.WindowState = WindowState.Normal;
				this.ShowInTaskbar = true;
				this.Activate();
			};

			//create context menu for the tray icon
			System.Windows.Forms.ContextMenu cMenu = new System.Windows.Forms.ContextMenu();
			System.Windows.Forms.MenuItem cMenuItem = new System.Windows.Forms.MenuItem();

			//create menu item to exit the application
			cMenuItem.Index = 0;
			cMenuItem.Text = "E&xit";
			cMenuItem.Click += new EventHandler(( object Sender, EventArgs e ) =>
			{
				m_close = true;
				this.Close();
			});

			cMenu.MenuItems.Add(cMenuItem);

			m_trayIcon.ContextMenu = cMenu;
		}

		private void _RemoveTrayIcon()
		{
			if ( m_trayIcon != null )
			{
				m_trayIcon.Visible = false;
				m_trayIcon.Dispose();
				m_trayIcon = null;
			}
		}

		protected override void OnStateChanged( EventArgs e )
		{
			if(WindowState == System.Windows.WindowState.Minimized)
			{
				_Minimize();
			}
			base.OnStateChanged(e);
		}

		protected override void OnSourceInitialized( EventArgs e )
		{
			base.OnSourceInitialized(e);

			//add hook to the app's main message loop
			HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
			source?.AddHook(WndProc);
		}

		protected override void OnClosing( CancelEventArgs e )
		{
			//don't allow closing unless it's from the tray icon
			if ( m_close == false )
			{
				_Minimize();
				e.Cancel = true;
				return;
			}

			_RemoveTrayIcon();

			base.OnClosing(e);
		}

		/// <summary>
		/// hook to process messages from the application's main message loop
		/// </summary>
		private IntPtr WndProc( IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled )
		{
			switch(msg)
			{
				case 0x007e: //WM_DISPLAYCHANGE
					{
						//refresh the tray icon to fix icon blurring issue in windows 10
						_RemoveTrayIcon();
						_CreateTrayIcon();

						//notify listeners of the change
						OnDisplayChangeDetected();
						break;
					}
				default:break;
			}

			return IntPtr.Zero;
		}

		/// <summary>
		/// Prevents non-numeric input into the transition text boxes
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void TransitionTextBox_PreviewTextInput( object sender, TextCompositionEventArgs e )
		{
			int result = 0;
			e.Handled = !Int32.TryParse(e.Text, out result);
		}

		private void LocationTextBox_PreviewTextInput( object sender, TextCompositionEventArgs e )
		{
			//allow inputting negative and decimal numbers
			if( e.Text.Equals("-") || e.Text.Equals(".") )
			{
				e.Handled = false;
				return;
			}

			double result = 0;
			e.Handled = !Double.TryParse(e.Text, out result);
		}

		private void SettingPanelCloseButton_Click( object sender, RoutedEventArgs e )
		{
			SettingsPanel.Visibility = Visibility.Hidden;
		}

		private void OpenSettingsPanelButton_Click( object sender, RoutedEventArgs e )
		{
			SettingsPanel.Visibility = Visibility.Visible;
		}

		private void LocationSearchButton_Click( object sender, RoutedEventArgs e )
		{
			if ( userLocationTextBox.Text.Length == 0 ) return;

			string loc = userLocationTextBox.Text;
			loc = loc.Replace(" ", "+");

			string url = "https://www.google.com/search?q=" + loc + "+coordinates";

			System.Diagnostics.Process.Start(url);
		}

		private void LocationTextBox_Pasting( object sender, DataObjectPastingEventArgs e )
		{
			if(e.DataObject.GetDataPresent(typeof(String)))
			{
				String text = (String)e.DataObject.GetData(typeof(string));
				double result = 0;
				if( !Double.TryParse(text, out result))
				{
					e.CancelCommand();
				}
			}
			else
			{
				e.CancelCommand();
			}
		}
	}
}
