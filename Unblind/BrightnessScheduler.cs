using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unblind
{

	using SystemDebugger = System.Diagnostics.Debug;

	public delegate void ScheduledBrightnessChange( uint targetBrightness, double timeLeft );

	public class BrightnessScheduler : ViewModelBase
	{

		public event ScheduledBrightnessChange OnBrightnessChange;

		//timer to trigger the brightness change event at the corrent time
		private System.Timers.Timer m_scheduleTimer;

		//the start of daytime and nighttime
		private TimeSpan m_daytime;
		private TimeSpan m_nighttime;

		//whether it is currently day or night, based on daytime and nighttime values
		private TimePeriod m_currentTimePeriod;

		private double m_nightBrightness;
		private double m_dayBrightness;

		//how long it should take, in milliseconds, to transition from day to night and back
		private double m_nightToDayTransitionDuration;
		private double m_dayToNightTransitionDuration;

		public double NightBrightness
		{
			get { return m_nightBrightness; }
			set
			{
				m_nightBrightness = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		public double DayBrightness
		{
			get { return m_dayBrightness; }
			set
			{
				m_dayBrightness = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		public TimePeriod CurrentTimePeriod
		{
			get { return m_currentTimePeriod; }
			private set
			{
				m_currentTimePeriod = value;
				OnPropertyChanged();
			}
		}

		public TimeSpan Daytime
		{
			get { return m_daytime; }
			set
			{
				m_daytime = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		public TimeSpan Nighttime
		{
			get { return m_nighttime; }
			set
			{
				m_nighttime = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// how long it will take to go from nighttime brightness to daytime brightness, in milliseconds
		/// </summary>
		public double NightToDayTransitionDuration
		{
			get { return m_nightToDayTransitionDuration; }
			set
			{
				m_nightToDayTransitionDuration = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// how long it will take to go from daytime brightness to nighttime brightness, in milliseconds
		/// </summary>
		public double DayToNightTransitionDuration
		{
			get { return m_dayToNightTransitionDuration; }
			set
			{
				m_dayToNightTransitionDuration = value;
				_ScheduleNextBrightnessChange();
				OnPropertyChanged();
			}
		}

		public BrightnessScheduler( TimeSpan daytime, TimeSpan nighttime, double nightBrightness, double dayBrightness, double dayToNightTransitionDuration, double nightToDayTransitionDuration )
		{
			m_daytime = daytime;
			m_nighttime = nighttime;
			m_nightBrightness = nightBrightness;
			m_dayBrightness = dayBrightness;
			m_nightToDayTransitionDuration = nightToDayTransitionDuration;
			m_dayToNightTransitionDuration = dayToNightTransitionDuration;
			m_currentTimePeriod = TimePeriod.None;

			/*create the timer that will control the scheduling of each transitio*/
			m_scheduleTimer = new System.Timers.Timer();
			m_scheduleTimer.AutoReset = false;
			m_scheduleTimer.Elapsed += new System.Timers.ElapsedEventHandler(( object source, System.Timers.ElapsedEventArgs e ) =>
			{
				_ScheduleNextBrightnessChange();
			});

			_ScheduleNextBrightnessChange();
		}

		public double TimeToNextPeriod()
		{
			double timeToNextPeriod = 0;
			TimePeriod nextPeriod = TimePeriod.None;
			_CalculateFollowingPeriod(DateTime.Now.TimeOfDay, out nextPeriod, out timeToNextPeriod);

			return timeToNextPeriod;
		}

		private void _ScheduleNextBrightnessChange()
		{
			m_scheduleTimer.Stop();

			double timeToNextPeriod = 0;
			TimePeriod nextPeriod = TimePeriod.None;

			_CalculateFollowingPeriod( DateTime.Now.TimeOfDay, out nextPeriod, out timeToNextPeriod);

			if(nextPeriod == TimePeriod.Day)
			{
				CurrentTimePeriod = TimePeriod.Night;
				double timeSincePeriodStarted = DateTime.Now.TimeOfDay.TotalMilliseconds - m_nighttime.TotalMilliseconds;
				if( timeSincePeriodStarted < -1 )
				{
					timeSincePeriodStarted += TimeSpan.FromHours(24).TotalMilliseconds;
				}
				if ( timeSincePeriodStarted < m_nightToDayTransitionDuration )
				{
					//within the transition period, so notify that brightness should transition to daytime levels now
					OnBrightnessChange?.Invoke((uint)m_nightBrightness, m_dayToNightTransitionDuration - timeSincePeriodStarted);
				}
				else
				{
					//notify that the current brightness should be at nighttime levels
					OnBrightnessChange?.Invoke((uint)m_nightBrightness, 0.0);
				}

				//schedule the upcoming transition from night to day
				m_scheduleTimer.Interval = timeToNextPeriod + 1;

			}
			else
			{
				CurrentTimePeriod = TimePeriod.Day;

				double timeSincePeriodStarted = DateTime.Now.TimeOfDay.TotalMilliseconds - m_daytime.TotalMilliseconds;
				if ( timeSincePeriodStarted < -1 )
				{
					timeSincePeriodStarted += TimeSpan.FromHours(24).TotalMilliseconds;
				}
				
				if ( timeSincePeriodStarted < m_dayToNightTransitionDuration )
				{
					//within the transition period, so notify that brightness should transition to nighttime levels now
					OnBrightnessChange?.Invoke((uint)m_dayBrightness, m_nightToDayTransitionDuration - timeSincePeriodStarted);
				}
				else
				{
					//notify that the current brightness should be at daytime levels
					OnBrightnessChange?.Invoke((uint)m_dayBrightness, 0.0);
				}

				//schedule the upcoming transition from day to night
				m_scheduleTimer.Interval = timeToNextPeriod + 1;
			}

			m_scheduleTimer.Start();

		}

		private void _CalculateFollowingPeriod( TimeSpan startTime, out TimePeriod period, out double timeToPeriod)
		{
			double currentTimeMS = startTime.TotalMilliseconds;

			double timeUntilNight = m_nighttime.TotalMilliseconds - currentTimeMS;
			if ( timeUntilNight <= -1 )
			{
				timeUntilNight = (m_nighttime.TotalMilliseconds + TimeSpan.FromHours(24).TotalMilliseconds) - currentTimeMS;
			}

			double timeUntilDay = m_daytime.TotalMilliseconds - currentTimeMS;
			if ( timeUntilDay <= -1 )
			{
				timeUntilDay = (m_daytime.TotalMilliseconds + TimeSpan.FromHours(24).TotalMilliseconds) - currentTimeMS;
			}

			if ( timeUntilDay < timeUntilNight )
			{ //daytime comes sooner than nighttime, meaning it's currently night
				period = TimePeriod.Day;
				timeToPeriod = timeUntilDay;
			}
			else
			{ //daytime comes later than nighttime, meaning it's currently day
				period = TimePeriod.Night;
				timeToPeriod = timeUntilNight;
			}
		}

	}
}