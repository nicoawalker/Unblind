using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Unblind
{

	using MonitorHandle = IntPtr;
	using DisplayCollection = List<Display>;

	public class Display
	{
		private object locker;

		public readonly DisplayController.PhysicalMonitorDesc Handle;

		public readonly uint ID;
		public readonly uint CapabilityFlags;
		public readonly uint ColorTemperatureFlags;
		public readonly uint MinBrightness;
		public readonly uint MaxBrightness;

		public uint m_currentBrightness;
		public uint CurrentBrightness
		{
			get { return m_currentBrightness; }
			set
			{
				lock ( locker ) m_currentBrightness = value;
			}
		}

		public bool IsValid;
		
		public Display(uint id, DisplayController.PhysicalMonitorDesc handle, uint minBrightness, uint maxBrightness, uint currentBrightness, uint capabilityFlags, uint colorTempFlags)
		{
			locker = new object();

			ID = id;
			Handle = handle;
			MinBrightness = minBrightness;
			MaxBrightness = maxBrightness;
			m_currentBrightness = currentBrightness;
			ColorTemperatureFlags = colorTempFlags;
			CapabilityFlags = capabilityFlags;

			IsValid = true;
		}

		public bool SupportsBrightness()
		{
			return (CapabilityFlags & 0x2) != 0;
		}
	}

	public delegate void DisplayRefresh( DisplayController controller );

	public sealed class DisplayController
	{
		private enum DisplayChangeType { Brightness = 0 };

		private const int MC_CAPS_BRIGHTNESS = 0x2;
		private const int SM_CMONITORS = 80;

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool DestroyPhysicalMonitors( uint dwPhysicalMonitorArraySize, [Out] PhysicalMonitorDesc[] pPhysicalMonitorArray );

		[DllImport("User32.dll", SetLastError = true)]
		private static extern bool EnumDisplayMonitors( IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData );

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool GetMonitorBrightness( IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness );

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool GetMonitorCapabilities( IntPtr hMonitor, out uint pdwMonitorCapabilities, out uint pdwSupportedColorTemperatures );

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR( IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors );

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool GetPhysicalMonitorsFromHMONITOR( IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PhysicalMonitorDesc[] pPhysicalMonitorArray );

		[DllImport("dxva2.dll", SetLastError = true)]
		private static extern bool SetMonitorBrightness( IntPtr hMonitor, uint dwNewBrightness );

		[DllImport("User32.dll", SetLastError = false)]
		private static extern int GetSystemMetrics( int nIndex );

		private delegate bool MonitorEnumProc( IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor, IntPtr dwData );

		[StructLayout(LayoutKind.Sequential)] //force members to be laid out in unmanaged memory in the order they appear here
		public struct PhysicalMonitorDesc
		{
			public MonitorHandle hPhysicalMonitor; //monitor handle
			[MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U2, SizeConst = 128)]
			public char[] szPhysicalMonitorDescription; //text description of the monitor
		}

		//nested instance to enable DisplayController to be a singleton
		private class NestedInstance
		{
			//explicit static constructor so compiler doesnt mark type as beforefieldinit
			static NestedInstance() { }

			internal static readonly DisplayController instance = new DisplayController();
		}

		private class DisplayChange
		{
			public Display TargetDisplay;
			public DisplayChangeType Type;
		}

		private class BrightnessDisplayChange : DisplayChange
		{
			public uint Brightness;

			public BrightnessDisplayChange( Display targetDisplay, uint brightness)
			{
				Type = DisplayChangeType.Brightness;
				Brightness = brightness;
				TargetDisplay = targetDisplay;
			}
		}

		private DisplayCollection m_attachedDisplays;
		public DisplayCollection AttachedDisplays
		{
			get { return new DisplayCollection(m_attachedDisplays); }
			private set
			{
				m_attachedDisplays = value;
			}
		}

		private bool m_integratedDisplaySupported;
		public bool IntegratedDisplaySupported
		{
			get { return m_integratedDisplaySupported; }
		}

		//lock to ensure only one call to _SetMonitorBrightness is invoked at a time
		//private object m_brightnessLock;
		
		private object m_displayListLock;
		private object m_displayChangeQueueLock;

		private AutoResetEvent m_newDisplayChangeEvent;

		private ManagementBaseObject m_wmiBrightnessControls;
		private ManagementObject m_wmiBrightnessSetter;

		private bool m_shutdown;

		private List<DisplayChange> m_displayChangeQueue;

		//notifies listeners when the display list has been refreshed
		public event DisplayRefresh OnDisplayRefesh;

		/*initialization task, allowing consumers to wait for initialization to finish*/
		public Task Initialization { get; private set; }

		public static DisplayController Instance { get { return NestedInstance.instance; } }

		private DisplayController()
		{
			m_attachedDisplays = new DisplayCollection();
			//m_brightnessLock = new object();
			m_displayListLock = new object();
			m_displayChangeQueueLock = new object();
			m_shutdown = false;
			m_newDisplayChangeEvent = new AutoResetEvent(false);
			m_displayChangeQueue = new List<DisplayChange>();
			m_wmiBrightnessControls = null;
			m_wmiBrightnessSetter = null;
			m_integratedDisplaySupported = false;

			_InitializeWMIBrightness();

			if ( m_wmiBrightnessControls != null && m_wmiBrightnessSetter != null)
			{
				m_integratedDisplaySupported = true;
				System.Diagnostics.Debug.WriteLine("Integrated displays are supported on this system");

			}else
			{
				System.Diagnostics.Debug.WriteLine("Integrated displays are not supported on this system");
			}

			Initialization = Task.Run(() =>
			{
				DisplayCollection displays = _GetAttachedDisplays();

				lock ( m_displayListLock ) m_attachedDisplays = displays;
			});

			Task.Run(() => _DisplayChangeApplicatorAsync());
		}

		~DisplayController()
		{
			//release all monitor handles
			lock ( m_displayListLock )
			{
				_DestroyPhysicalMonitors(m_attachedDisplays);

				foreach ( Display display in m_attachedDisplays )
				{
					display.IsValid = false;
				}
				m_attachedDisplays.Clear();
			}

			m_shutdown = true;
			m_newDisplayChangeEvent.Set();
		}

		private void _InitializeWMIBrightness()
		{
			try
			{
				//query microsoft services for the WMI brightness control methods
				ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightness");
				ManagementObjectCollection managementObjects = managementObjectSearcher.Get();
				var objectEnum = managementObjects.GetEnumerator();
				objectEnum.MoveNext();
				m_wmiBrightnessControls = objectEnum.Current;

				//we have the brightness controls object, now we need an instance from it for setting the brightness
				var instanceName = (string)m_wmiBrightnessControls["InstanceName"];
				m_wmiBrightnessSetter = new ManagementObject("root\\WMI", "WmiMonitorBrightnessMethods.InstanceName='" + instanceName + "'", null);

			} catch ( Exception e )
			{
				Console.WriteLine("Exception initializing WMI: " + e.Message);
				m_wmiBrightnessControls = null;
				m_wmiBrightnessSetter = null;
			}
		}

		private DisplayCollection _GetAttachedDisplays()
		{
			DisplayCollection attachedDisplays = new DisplayCollection();

			List<MonitorHandle> monitorHandles = _EnumerateAllMonitors();

			foreach(MonitorHandle handle in monitorHandles)
			{
				List<PhysicalMonitorDesc> physicalMonitors = _GetPhysicalMonitors(handle);

				foreach(PhysicalMonitorDesc physicalMonitor in physicalMonitors)
				{
					/*get the capabilities and supported color temperature of each display*/
					uint capabilitiesFlags = 0, colorTempFlags = 0;
					_QueryMonitorCapabilities(physicalMonitor, out capabilitiesFlags, out colorTempFlags);

					/*get the min, max, and current brightness from the display*/
					uint maxBrightness = 0, minBrightness = 0, currentBrightness = 0;
					_QueryMonitorBrightnessInfo(physicalMonitor, out minBrightness, out maxBrightness, out currentBrightness);

					uint id = 0;
					lock ( m_displayListLock )
					{
						if ( m_attachedDisplays.Count > 0 ) id = m_attachedDisplays.Last().ID + 1;
						attachedDisplays.Add(new Display(id, physicalMonitor, minBrightness, maxBrightness, currentBrightness, capabilitiesFlags, colorTempFlags));
					}
				}
			}

			return attachedDisplays;
		}

		/// <summary>
		/// closes a list of display monitor handles
		/// </summary>
		/// <param name="systemDisplays"></param>
		private void _DestroyPhysicalMonitors( DisplayCollection displays )
		{
			PhysicalMonitorDesc[] monitorArray = new PhysicalMonitorDesc[displays.Count];

			uint index = 0;
			foreach ( Display display in displays )
			{
				if ( display.Handle.hPhysicalMonitor != IntPtr.Zero )
				{
					monitorArray[index++] = display.Handle;
				}
			}

			if ( !DestroyPhysicalMonitors((uint)monitorArray.Length, monitorArray) )
			{
				try
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());

				}catch(Win32Exception e)
				{
					System.Diagnostics.Debug.WriteLine("Error destroying physical monitors: " + e.Message);
				}
			}
		}

		/// <summary>
		/// Enumerates all physical and virtual displays connected to the system
		/// </summary>
		/// <returns>A list containing information about each display</returns>
		private List<MonitorHandle> _EnumerateAllMonitors()
		{
			List<MonitorHandle> monitorHandles = new List<MonitorHandle>();

			if ( !EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate ( MonitorHandle hMonitor, IntPtr hdc, ref Rect lprcMonitor, IntPtr dwData )
				{
					if ( hMonitor != IntPtr.Zero )
					{
						monitorHandles.Add(hMonitor);
					}

					return true;
				},
				IntPtr.Zero) )
			{
				try
				{
					throw new Win32Exception(Marshal.GetLastWin32Error());
				}
				catch ( Win32Exception e )
				{
					System.Diagnostics.Debug.WriteLine("Error enumerating physical monitors: " + e.Message);
					MessageBox.Show("An error occured while scanning your attached displays. Please try restarting Unblind.\n\nError: " + e.Message);
				}
			}

			return monitorHandles;
		}

		/// <summary>
		/// Retrieves the physical monitors associated with a monitor handle
		/// </summary>
		/// <param name="monitor">Monitor handle</param>
		/// <returns>A list of physical monitors associated with the monitor handle</returns>
		private List<PhysicalMonitorDesc> _GetPhysicalMonitors( IntPtr monitor )
		{
			uint numMonitors;
			if ( !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out numMonitors) )
			{
				System.Diagnostics.Debug.WriteLine("Error getting physical monitor count");

				return new List<PhysicalMonitorDesc>();
				//throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			PhysicalMonitorDesc[] monitorArray = new PhysicalMonitorDesc[numMonitors];
			if ( !GetPhysicalMonitorsFromHMONITOR(monitor, numMonitors, monitorArray) )
			{
				System.Diagnostics.Debug.WriteLine("Error getting physical monitor handles");

				return new List<PhysicalMonitorDesc>();
				//throw new Win32Exception(Marshal.GetLastWin32Error());
			}

			foreach ( PhysicalMonitorDesc mon in monitorArray )
			{
				System.Diagnostics.Debug.WriteLine("Physical monitor handle: {0}", mon.hPhysicalMonitor);
			}

			return new List<PhysicalMonitorDesc>(monitorArray);
		}

		/// <summary>
		/// Retrieves brightness information about a monitor. This method takes about 40 milliseconds to return
		/// </summary>
		/// <param name="monitor">physical monitor to test</param>
		private void _QueryMonitorBrightnessInfo( PhysicalMonitorDesc monitorDesc, out uint min, out uint max, out uint current )
		{
			bool success = GetMonitorBrightness(monitorDesc.hPhysicalMonitor, out min, out current, out max);

			if ( success == false )
			{
				/*call can sporadically fail, so try one more time*/
				success = GetMonitorBrightness(monitorDesc.hPhysicalMonitor, out min, out current, out max);

				if(success == false)
				{
					min = max = current = 0;

					//throw new Win32Exception(Marshal.GetLastWin32Error());
				}
			}
		}

		/// <summary>
		/// Sets the brightness of a given display. This method takes upwards of 40 milliseconds to return
		/// </summary>
		/// <param name="monitor">physical monitor</param>
		/// <param name="brightness">New brightness value. Value will be clamped to the brightness range supported by the monitor</param>
		/// <returns>nothing if successful. throws Win32Exception on error</returns>
		private int _SetDisplayBrightness( PhysicalMonitorDesc monitor, uint brightness )
		{
			if ( !SetMonitorBrightness(monitor.hPhysicalMonitor, brightness) )
			{
				/*call can sporadically fail, so try again just in case*/
				if ( !SetMonitorBrightness(monitor.hPhysicalMonitor, brightness) )
				{
					return Marshal.GetLastWin32Error();
				}
			}

			return 0;
		}

		/// <summary>
		/// Tests whether a monitor supports having its brightness changed, and its supported color temperatures
		/// </summary>
		/// <param name="monitor">monitor to check</param>
		/// <returns></returns>
		private void _QueryMonitorCapabilities( PhysicalMonitorDesc monitor, out uint capabilitiesFlags, out uint colorTempFlags )
		{
			if ( !GetMonitorCapabilities(monitor.hPhysicalMonitor, out capabilitiesFlags, out colorTempFlags) )
			{
				capabilitiesFlags = colorTempFlags = 0;
			}
		}

		/// <summary>
		/// Async method responsible for applying queued changes to the displays
		/// </summary>
		private void _DisplayChangeApplicatorAsync()
		{
			DisplayChange displayChange;

			while ( !m_shutdown )
			{
				m_newDisplayChangeEvent.WaitOne();

				if ( m_shutdown ) return;

				while ( m_displayChangeQueue.Count > 0 )
				{
					lock ( m_displayChangeQueueLock )
					{
						displayChange = m_displayChangeQueue.First();
						m_displayChangeQueue.RemoveAt(0);
					}

					if ( displayChange == null ) continue;

					if(displayChange.Type == DisplayChangeType.Brightness)
					{
						BrightnessDisplayChange brightnessChange = displayChange as BrightnessDisplayChange;
						if ( brightnessChange == null ) continue;

						if ( brightnessChange.TargetDisplay != null )
						{
							//set brightness of attached display, if there is one
							_SetDisplayBrightness(brightnessChange.TargetDisplay.Handle, brightnessChange.Brightness);

						}else
						{
							//set brightness of integrated displays, if supported
							if ( m_wmiBrightnessSetter != null )
							{
								try
								{
									var wmiBrightnessParams = m_wmiBrightnessSetter.GetMethodParameters("WmiSetBrightness");
									wmiBrightnessParams["Brightness"] = brightnessChange.Brightness;
									wmiBrightnessParams["Timeout"] = 0;
									m_wmiBrightnessSetter.InvokeMethod("WmiSetBrightness", wmiBrightnessParams, null);
								}
								catch ( Exception e )
								{
									MessageBox.Show("An integrated display appears to be present, but an error occured trying to set its brightness: " + e.Message);
									System.Diagnostics.Debug.WriteLine("An integrated display appears to be present, but an error occured trying to set its brightness: " + e.Message);
								}
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Gets the number of displays that are connected
		/// </summary>
		public uint GetDisplayCount()
		{
			return (uint)m_attachedDisplays.Count;
		}

		public Display GetDisplay( uint displayIndex )
		{
			lock(m_displayListLock) return m_attachedDisplays[(int)displayIndex];
		}

		/// <summary>
		/// Queues a brightness change for a display. Only one brightness change can be processed at once so the change may not occur immediately
		/// </summary>
		/// <param name="display">display to set brightness of</param>
		/// <param name="brightness">new brightness value. value will be clamped to the display's mininum and maximum values</param>
		/// <returns></returns>
		public void SetAttachedDisplayBrightness( Display display, uint brightness )
		{
			if ( display == null ) return;

			if ( display.IsValid == false )
			{
				System.Diagnostics.Debug.WriteLine("Tried to set brightness on invalid display!");
				return;
			}

			brightness = MathHelpers.ClampValue(brightness, display.MinBrightness, display.MaxBrightness);

			lock ( m_displayChangeQueueLock )
			{
				//if a brightness change is already queued for this display, replace it with this new one, otherwise queue up a new one
				bool replaced = false;
				for ( int i = 0; i < m_displayChangeQueue.Count; i++ )
				{
					if ( m_displayChangeQueue[i].TargetDisplay != null && m_displayChangeQueue[i].Type == DisplayChangeType.Brightness &&
						m_displayChangeQueue[i].TargetDisplay.Handle.hPhysicalMonitor == display.Handle.hPhysicalMonitor )
					{
						m_displayChangeQueue[i] = new BrightnessDisplayChange(display, brightness);
						replaced = true;
					}
				}

				if ( !replaced ) m_displayChangeQueue.Add(new BrightnessDisplayChange(display, brightness));
			}

			m_newDisplayChangeEvent.Set();
		}

		public void SetIntegratedDisplayBrightness( uint brightness )
		{
			if ( m_wmiBrightnessControls == null ) return;

			lock ( m_displayChangeQueueLock )
			{
				/*replace any existing brightness change, or create a new one*/
				bool replaced = false;
				for ( int i = 0; i < m_displayChangeQueue.Count; i++ )
				{
					if ( m_displayChangeQueue[i].Type == DisplayChangeType.Brightness && m_displayChangeQueue[i].TargetDisplay == null )
					{
						m_displayChangeQueue[i] = new BrightnessDisplayChange(null, brightness);
						replaced = true;
					}
				}

				if(!replaced) m_displayChangeQueue.Add(new BrightnessDisplayChange(null, brightness));
			}

			m_newDisplayChangeEvent.Set();
		}

		/// <summary>
		/// Performs a complete refresh of the displays. All current display handles will be invalidated
		/// </summary>
		public Task RefreshAsync()
		{
			return Task.Run(async () =>
			{
				//ensure refresh doesn't happen before initalization
				await Initialization;

				DisplayCollection displays = _GetAttachedDisplays();

				lock ( m_displayListLock )
				{
					foreach ( Display display in m_attachedDisplays )
					{
						display.IsValid = false;
					}

					m_attachedDisplays = displays;
				}

				OnDisplayRefesh(DisplayController.Instance);
			});
		}

	}
}
