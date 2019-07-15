using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace Unblind
{

	public enum TimePeriod { Night = 0, Day, None };

	public class MainWindowViewModel : ViewModelBase
	{
		//name of the shortcut that will be placed in the user's Startup folder (if StartWithWindows is enabled)
		private const string m_shortcutName = "Unblind.lnk";

		//value (in percent) used to temporarily set the brightness as a preview while the user slides the brightness slider
		private const double m_previewDuration = 500.0;

		private const int m_versionMajor = 1;
		private const int m_versionMinor = 0;
		private const int m_versionIteration = 2;

		//locking object to provide thread safety
		private object m_vmLock;

		//timer to reset the brightness back to current value after previewing
		private System.Timers.Timer m_previewResetTimer;

		//timer the runs synchronized with the system clock, with intervals every second
		private System.Timers.Timer m_systemClockTimer;

		private BrightnessScheduler m_brightnessScheduler;

		private DimmerStatus m_currentBrightnessTransitionStatus;
		private BrightnessDimmer m_brightnessDimmer;

		private string m_windowTitle;

		//string representation of the time remaining until day/night
		private string m_timeToNextPeriod;

		//the location coordinates of the user
		private string m_latitude;
		private string m_longitude;

		//counter for the clock method to measure elapsed time
		private int m_clockElapsedTime;

		//highest display brightness that is supported by all displays
		private uint m_maxDisplayBrightness;
		//lowest display brightness that is supported by all displays
		private uint m_minDisplayBrightness;
		//the actual brightness value at the current time
		private uint m_currentBrightness;

		private bool m_locationEnabled;
		private bool m_multipleDisplaysConnected;
		private bool m_noSupportDetected; //true if any display has indicated that it doesn't support brightness control
		private bool m_firstRun;
		private bool m_startWithWindows;
		private bool m_startMinimized;


		#region Public Properties

		public BrightnessScheduler BrightnessScheduler
		{
			get { return m_brightnessScheduler; }
			private set { m_brightnessScheduler = value; }
		}
		
		public bool LocationEnabled
		{
			get { return m_locationEnabled; }
			set
			{
				m_locationEnabled = value;
				OnPropertyChanged();
				DataAccess.AppSettingConfigurator.AddUpdateSetting("LocationEnabled", value.ToString());

				_UpdateAndApplyLocation();
			}
		}
		
		public bool MultipleDisplaysConnected
		{
			get { return m_multipleDisplaysConnected; }
			set { m_multipleDisplaysConnected = value; OnPropertyChanged(); }
		}
		
		public bool NoSupportDetected
		{
			get { return m_noSupportDetected; }
			set { m_noSupportDetected = value; OnPropertyChanged(); }
		}
		
		public DimmerStatus CurrentBrightnessTransitionStatus
		{
			get { return m_currentBrightnessTransitionStatus; }
			set { m_currentBrightnessTransitionStatus = value; OnPropertyChanged(); }
		}
		
		public TimePeriod CurrentTimePeriod
		{
			get { return m_brightnessScheduler.CurrentTimePeriod; }
		}
		
		public uint CurrentBrightness
		{
			get { return m_currentBrightness; }
			set
			{
				m_currentBrightness = value;
				OnPropertyChanged();
			}
		}
		
		public string TimeToNextPeriod
		{
			get { return m_timeToNextPeriod; }
			set { m_timeToNextPeriod = value; OnPropertyChanged(); }
		}
		
		public uint MaxDisplayBrightness
		{
			get { return m_maxDisplayBrightness; }
			set { m_maxDisplayBrightness = value; OnPropertyChanged(); }
		}
		
		public uint MinDisplayBrightness
		{
			get { return m_minDisplayBrightness; }
			set { m_minDisplayBrightness = value; OnPropertyChanged(); }
		}

		public string WindowTitle
		{
			get { return m_windowTitle; }
			set { m_windowTitle = value; OnPropertyChanged(); }
		}
		
		public string Latitude
		{
			get { return m_latitude; }
			set
			{
				m_latitude = value;
				OnPropertyChanged();
				_UpdateAndApplyLocation();
			}
		}
		
		public string Longitude
		{
			get { return m_longitude; }
			set
			{
				m_longitude = value;
				OnPropertyChanged();
				_UpdateAndApplyLocation();
			}
		}

		public bool StartWithWindows
		{
			get { return m_startWithWindows; }
			set
			{
				m_startWithWindows = value;
				OnPropertyChanged();

				DataAccess.AppSettingConfigurator.AddUpdateSetting("StartWithWindows", m_startWithWindows.ToString());

				//create or destroy the startup shortcut
				if (m_startWithWindows)
				{
					_CreateStartupShortcut();
					//also enable starting minimized
					StartMinimized = true;

				}
				else
				{
					_DeleteStartupShortcut();

					//also disable starting minimized
					StartMinimized = false;
				}
			}
		}

		public bool StartMinimized
		{
			get { return m_startMinimized; }
			set
			{
				m_startMinimized = value;
				OnPropertyChanged();

				DataAccess.AppSettingConfigurator.AddUpdateSetting("StartMinimized", m_startMinimized.ToString());
			}
		}

		#endregion


		public MainWindowViewModel( MainWindow window )
		{
			m_previewResetTimer = new System.Timers.Timer();
			m_systemClockTimer = new System.Timers.Timer();

			m_vmLock = new object();

			m_latitude = "";
			m_longitude = "";
			m_timeToNextPeriod = "--:--:--";
			m_windowTitle = "Unblind";
			m_currentBrightnessTransitionStatus = DimmerStatus.Idle;
			m_clockElapsedTime = 0;
			m_currentBrightness = 100;
			m_maxDisplayBrightness = 0;
			m_minDisplayBrightness = 1;
			m_firstRun = false;
			m_locationEnabled = false;
			m_noSupportDetected = false;
			m_multipleDisplaysConnected = false;
			m_startWithWindows = false;
			m_startMinimized = false;

			m_brightnessDimmer = new BrightnessDimmer();
			m_brightnessDimmer.OnBrightnessChange += _OnBrightnessChangeEvent;

			m_brightnessScheduler = new BrightnessScheduler( TimeSpan.FromHours(7), TimeSpan.FromHours(18), 50, 90, 60000, 60000);
			m_brightnessScheduler.OnBrightnessChange += _OnScheduledBrightnessEvent;
			m_brightnessScheduler.PropertyChanged += _OnSchedulerPropertyChanged;


			//add listener for display changes (display plugged in/unplugged)
			window.OnDisplayChangeDetected += () =>
			{
				_OnDisplayChangeDetected();
			};

			m_previewResetTimer.AutoReset = false;
			m_previewResetTimer.Interval = m_previewDuration;
			m_previewResetTimer.Elapsed += new System.Timers.ElapsedEventHandler(( object source, System.Timers.ElapsedEventArgs e ) =>
			{
				m_brightnessDimmer.AdjustBrightness(m_currentBrightness, m_currentBrightness, 0.0);
			});

			m_systemClockTimer.AutoReset = true;
			m_systemClockTimer.Interval = 1000;
			m_systemClockTimer.Elapsed += _ClockTick;

			_InitializeApp(window);
		}

		~MainWindowViewModel()
		{
			if ( m_firstRun )
			{
				DataAccess.AppSettingConfigurator.AddUpdateSetting("FirstRun", "False");
			}
		}

		private void _InitializeApp(Window window)
		{
			DisplayController dispController = DisplayController.Instance;

			_LoadAppData();

			if(m_startMinimized)
			{
				window.Hide();
				window.ShowInTaskbar = false;
			}

			/*wait for the display controller to finish initializing before proceeding.
			 if something went wrong this call will throw a win32exception*/
			dispController.Initialization.Wait();

			WindowTitle = "Unblind " + m_versionMajor.ToString() + "." + m_versionMinor.ToString() + "." + m_versionIteration.ToString();

			if ( dispController.AttachedDisplays.Count > 0 )
			{
				bool supportsBrightness = true;
				foreach ( Display display in dispController.AttachedDisplays )
				{
					if ( m_firstRun && supportsBrightness && display.SupportsBrightness() == false )
					{
						supportsBrightness = false;
						MessageBox.Show("It appears that one or more of your displays do not support the DDC/CI protocol required to change the brightness via software. Unblind may or may not be able to control these displays.", "Hmm...", MessageBoxButton.OK);
					}

					//determine the highest minimum brightness and lowest maximum brightness among all displays and use these as the brightness limits
					if ( display.MinBrightness > m_minDisplayBrightness ) MinDisplayBrightness = MathHelpers.Greater(display.MinBrightness, (uint)1);
					if ( display.MaxBrightness > m_maxDisplayBrightness ) MaxDisplayBrightness = MathHelpers.Greater(display.MaxBrightness, (uint)1);
					if ( display.SupportsBrightness() == false ) NoSupportDetected = true;
				}

			}else if(dispController.IntegratedDisplaySupported == true)
			{
				MinDisplayBrightness = 0;
				MaxDisplayBrightness = 100;
			}

			if ( dispController.AttachedDisplays.Count > 1 || (dispController.AttachedDisplays.Count >= 1 && dispController.IntegratedDisplaySupported == true) ) MultipleDisplaysConnected = true;

			CurrentBrightnessTransitionStatus = DimmerStatus.Idle;

			_UpdateTimeToNextPeriod();

			m_systemClockTimer.Start();
		}

		private void _LoadAppData()
		{
			m_brightnessScheduler.Daytime = DateTime.Parse(DataAccess.AppSettingConfigurator.ReadSetting("DaytimeStart")).TimeOfDay;
			m_brightnessScheduler.Nighttime = DateTime.Parse(DataAccess.AppSettingConfigurator.ReadSetting("NighttimeStart")).TimeOfDay;

			m_brightnessScheduler.NightToDayTransitionDuration = Int32.Parse(DataAccess.AppSettingConfigurator.ReadSetting("NightToDayTransitionTime")) * 60000;
			m_brightnessScheduler.DayToNightTransitionDuration = Int32.Parse(DataAccess.AppSettingConfigurator.ReadSetting("DayToNightTransitionTime")) * 60000;

			m_brightnessScheduler.NightBrightness = (uint)Int32.Parse(DataAccess.AppSettingConfigurator.ReadSetting("NightBrightness"));
			m_brightnessScheduler.DayBrightness = (uint)Int32.Parse(DataAccess.AppSettingConfigurator.ReadSetting("DayBrightness"));

			m_firstRun = DataAccess.AppSettingConfigurator.ReadSetting("FirstRun").Equals("True");
			m_startMinimized = DataAccess.AppSettingConfigurator.ReadSetting("StartMinimized").Equals("True");
			StartWithWindows = DataAccess.AppSettingConfigurator.ReadSetting("StartWithWindows").Equals("True");

			LocationEnabled = DataAccess.AppSettingConfigurator.ReadSetting("LocationEnabled").Equals("True");
			Latitude = DataAccess.AppSettingConfigurator.ReadSetting("LocationLatitude");
			Longitude = DataAccess.AppSettingConfigurator.ReadSetting("LocationLongitude");
		}

		private double _CalculateTimeToNextPeriod( TimePeriod currentPeriod )
		{
			double time = 0.0;

			if ( currentPeriod == TimePeriod.Day )
			{
				time = (m_brightnessScheduler.Nighttime - DateTime.Now.ToUniversalTime().TimeOfDay).TotalMilliseconds;

			} else
			{
				time = (m_brightnessScheduler.Daytime - DateTime.Now.ToUniversalTime().TimeOfDay).TotalMilliseconds;
			}

			while ( time <= 0 )
			{//want to shedule the next occurence
				time += TimeSpan.FromHours(24).TotalMilliseconds;
			}

			return time;
		}

		private void _OnScheduledBrightnessEvent( uint targetBrightness, double timeLeft )
		{
			m_brightnessDimmer.Stop();
			m_brightnessDimmer.AdjustBrightness(m_currentBrightness, targetBrightness, timeLeft);
		}

		private void _OnSchedulerPropertyChanged( object sender, PropertyChangedEventArgs e )
		{
			BrightnessScheduler scheduler = sender as BrightnessScheduler;
			if ( scheduler == null ) return;

			switch ( e.PropertyName )
			{
				case "NightBrightness":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("NightBrightness", scheduler.NightBrightness.ToString());
					break;
				case "DayBrightness":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("DayBrightness", scheduler.DayBrightness.ToString());
					break;
				case "Daytime":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("DaytimeStart", scheduler.Daytime.ToString());
					break;
				case "Nighttime":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("NighttimeStart", scheduler.Nighttime.ToString());
					break;
				case "NightToDayTransitionDuration":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("NightToDayTransitionTime", (scheduler.NightToDayTransitionDuration / 60000.0).ToString());
					break;
				case "DayToNightTransitionDuration":
					DataAccess.AppSettingConfigurator.AddUpdateSetting("DayToNightTransitionTime", (scheduler.DayToNightTransitionDuration / 60000.0).ToString());
					break;
				default: break;
			}
		}

		private void _OnBrightnessChangeEvent( uint newBrightness, int deltaBrightness )
		{
			CurrentBrightness = newBrightness;
		}

		/// <summary>
		/// Called when a change in one or more displays is detected, such as being plugged in or unplugged
		/// </summary>
		private void _OnDisplayChangeDetected()
		{
			DisplayController.Instance?.RefreshAsync().ContinueWith(_ =>
			{
				//have to re-check whether there are multiple monitors and if all monitors support brightness changes
				DisplayController controller = DisplayController.Instance;
				MultipleDisplaysConnected = (controller.AttachedDisplays.Count > 1 || (controller.AttachedDisplays.Count == 1 && controller.IntegratedDisplaySupported == true)) ? true : false;
				NoSupportDetected = false;
				foreach ( Display display in controller.AttachedDisplays )
				{
					if ( display.SupportsBrightness() == false )
					{
						NoSupportDetected = true;
						break;
					}
				}

				if ( m_brightnessDimmer.Status == DimmerStatus.Idle )
				{
					m_brightnessDimmer.AdjustBrightness(m_currentBrightness, m_currentBrightness, 0.0);
				}
			});
		}

		/// <summary>
		/// Saves the current location to the configuration file and calculates and applies the time of sunset/sunrise
		/// also starts the location update timer, if it isn't already running
		/// </summary>
		private void _UpdateAndApplyLocation()
		{
			if ( m_locationEnabled )
			{
				double currentLatitude = 0.0;
				double currentLongitude = 0.0;

				if ( (Double.TryParse(m_latitude, out currentLatitude) && Double.TryParse(m_longitude, out currentLongitude)) )
				{
					DateTime sunrise, sunset;
					_CalculateSunriseSunset(currentLatitude, currentLongitude, out sunrise, out sunset);

					lock ( m_vmLock )
					{
						m_brightnessScheduler.Daytime = sunrise.TimeOfDay;
						m_brightnessScheduler.Nighttime = sunset.TimeOfDay;

						DataAccess.AppSettingConfigurator.AddUpdateSetting("LocationLatitude", m_latitude);
						DataAccess.AppSettingConfigurator.AddUpdateSetting("LocationLongitude", m_longitude);
					}
				}
			}
		}

		/// <summary>
		/// called once per second
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void _ClockTick( object sender, System.Timers.ElapsedEventArgs e )
		{
			//update location once per hour
			m_clockElapsedTime += 1000;
			if ( m_clockElapsedTime >= 3600000 )
			{
				m_clockElapsedTime -= 3600000;
				_UpdateAndApplyLocation();
			}

			lock ( m_vmLock )
			{
				_UpdateTimeToNextPeriod();
				CurrentBrightnessTransitionStatus = m_brightnessDimmer.Status;
			}
		}

		/// <summary>
		/// creates a shortcut (.lnk) in the user's Startup folder so that Unblind will start with windows
		/// </summary>
		private void _CreateStartupShortcut()
		{
			/*check if a shortcut has been created in the user's Startup folder, and create one if it hasn't*/
			Console.WriteLine(Environment.GetFolderPath(Environment.SpecialFolder.Startup));
			string shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + m_shortcutName;
			if ( File.Exists(shortcutPath) == false )
			{
				//code from https://stackoverflow.com/questions/234231/creating-application-shortcut-in-a-directory

				Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); //Windows Script Host Shell Object
				dynamic shell = Activator.CreateInstance(t);
				try
				{
					var lnk = shell.CreateShortcut(shortcutPath);
					try
					{
						lnk.TargetPath = System.AppDomain.CurrentDomain.BaseDirectory + @"\Unblind.exe";
						lnk.IconLocation = AppDomain.CurrentDomain.BaseDirectory + @"Resources\unblind.ico";
						Console.WriteLine(lnk.IconLocation);
						lnk.Save();

					}
					finally
					{
						Marshal.FinalReleaseComObject(lnk);
					}

				}catch(Exception e)
				{
					MessageBox.Show("Unblind could not create a shortcut in your startup folder. Please try again.\n\nError: " + e.Message, "Oh no!", MessageBoxButton.OK, MessageBoxImage.Error);
					StartWithWindows = false;
				}
				finally
				{
					Marshal.FinalReleaseComObject(shell);
				}
			}
		}

		/// <summary>
		/// removes the shortcut to Unblind in the user's Startup folder so that Unblind will no longer start with windows
		/// </summary>
		private void _DeleteStartupShortcut()
		{
			if ( File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + m_shortcutName) )
			{
				File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Startup) + @"\" + m_shortcutName);
			}
		}

		private void _UpdateTimeToNextPeriod()
		{
			TimeSpan nextPeriod = TimeSpan.FromMilliseconds(m_brightnessScheduler.TimeToNextPeriod());
			TimeToNextPeriod = new TimeSpan(nextPeriod.Hours, nextPeriod.Minutes, nextPeriod.Seconds).ToString();
		}

		private void _CalculateSunriseSunset(double latitude, double longitude, out DateTime sunrise, out DateTime sunset )
		{
			//equation from https://en.wikipedia.org/wiki/Sunrise_equation#Complete_calculation_on_Earth
			
			//calculate current julian day. don't want extra hours so we chop off any decimals from the current julian date
			double n = (double)((int)DateTimeToJulianDate(DateTime.Now.ToUniversalTime())) - 2451545.0 + 0.0008;

			//calculate  mean solar noon
			double msn = n - (longitude / 360.0);

			//calculate solar mean anomaly
			double sma = (357.5291 + 0.98560028 * msn) % 360;

			//equation of the center
			double center = (1.9148 * SinDegrees(sma)) + (0.02 * SinDegrees(2 * sma)) + (0.0003 * SinDegrees(3 * sma));

			//ecliptic longitude
			double elong = (sma + center + 180 + 102.9372) % 360;

			//solar transit
			double strans = 2451545.0 + msn + (0.0053 * SinDegrees(sma)) - (0.0069 * SinDegrees(2 * elong));

			//declination of the sun and hour-angle
			double declination = MathHelpers.RadiansToDegrees(Math.Asin(SinDegrees(elong) * SinDegrees(23.55)));

			//hour angle
			double hAngle = MathHelpers.RadiansToDegrees(Math.Acos((SinDegrees(-0.83) - SinDegrees(latitude) * SinDegrees(declination)) / (Math.Cos(MathHelpers.DegreesToRadians(latitude)) * Math.Cos(MathHelpers.DegreesToRadians(declination)))));

			//julian date of sunrise
			double rise = strans - (hAngle / 360);
			double set = strans + (hAngle / 360);

			sunrise = JulianDateToUTC(rise).ToLocalTime();
			sunset = JulianDateToUTC(set).ToLocalTime();
		}

		private DateTime JulianDateToUTC( double julian )
		{
			double sinceEpoch = julian - 2440587.5;
			return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(sinceEpoch);
		}

		private double DateTimeToJulianDate( DateTime time )
		{
			return time.ToOADate() + 2415018.5;
		}

		private double SinDegrees(double degrees)
		{
			return Math.Sin(MathHelpers.DegreesToRadians(degrees));
		}

	}
}