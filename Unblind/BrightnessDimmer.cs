using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Unblind
{

	public delegate void BrightnessAdjustmentCallback( uint newBrightness, int deltaBrightness );

	public enum DimmerStatus { Idle = 0, Dimming, Brightening };

	public class BrightnessDimmer
	{
		public object m_lock;

		public event BrightnessAdjustmentCallback OnBrightnessChange;

		private DimmerStatus m_status;

		//timer used to gradually dim from one brightness to another
		private System.Timers.Timer m_dimmerTimer;

		private uint m_currentBrightness;
		private uint m_targetBrightness;

		public DimmerStatus Status
		{
			get { return m_status; }
		}

		public BrightnessDimmer()
		{
			m_lock = new object();

			m_dimmerTimer = new System.Timers.Timer();
			m_dimmerTimer.AutoReset = true;
			m_dimmerTimer.Elapsed += new System.Timers.ElapsedEventHandler(( object source, System.Timers.ElapsedEventArgs e ) =>
			{
				int brightnessChange = (m_currentBrightness > m_targetBrightness) ? -1 : 1;

				lock ( m_lock )
				{
					m_currentBrightness = (brightnessChange == 1) ? m_currentBrightness + 1 : m_currentBrightness - 1;
				}

				SetDisplayBrightness(m_currentBrightness);

				OnBrightnessChange?.Invoke(m_currentBrightness, brightnessChange);

				if ( m_currentBrightness == m_targetBrightness )
				{
					this.Stop();
				}
			});

			m_status = DimmerStatus.Idle;

			m_currentBrightness = 0;
			m_targetBrightness = 0;
		}

		public void SetDisplayBrightness( uint brightness )
		{
			DisplayController dispController = DisplayController.Instance;
			
			//set the brightness of each attached display
			foreach ( Display display in dispController.AttachedDisplays )
			{
				if ( display.IsValid ) dispController.SetAttachedDisplayBrightness(display, brightness);
			}

			//also set the brightness of the integrated display, if there is one
			dispController.SetIntegratedDisplayBrightness(brightness);
		}

		/// <summary>
		/// adjusts the brightness from one value to another over a set period of time
		/// </summary>
		/// <param name="startBrightness">brightness to start at</param>
		/// <param name="targetBrightness">brightness to end at</param>
		/// <param name="interval">how long it should take, in milliseconds, to go from start to target brightness</param>
		public void AdjustBrightness( uint startBrightness, uint targetBrightness, double interval )
		{
			if ( startBrightness == targetBrightness || interval <= 0 )
			{
				SetDisplayBrightness(targetBrightness);

				OnBrightnessChange?.Invoke(targetBrightness, (int)targetBrightness - (int)startBrightness);

				return;
			}

			m_dimmerTimer.Stop();

			lock ( m_lock )
			{
				m_currentBrightness = startBrightness;
				m_targetBrightness = targetBrightness;
			}

			m_dimmerTimer.Interval = interval / (double)Math.Abs((int)startBrightness - (int)targetBrightness);

			m_dimmerTimer.Start();

			m_status = (startBrightness < targetBrightness) ? DimmerStatus.Brightening : DimmerStatus.Dimming;
		}

		public void Stop()
		{
			m_dimmerTimer.Stop();
			m_status = DimmerStatus.Idle;
		}

	}
}
