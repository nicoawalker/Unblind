using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration; //requires reference to System.Configuration.dll

namespace Unblind.DataAccess
{
	public static class AppSettingConfigurator
	{

		private static object m_lock = new object();

		public static bool AddUpdateSetting(string key, string value )
		{
			try
			{
				lock ( m_lock )
				{
					Configuration configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
					if ( configFile == null ) return false;

					KeyValueConfigurationCollection settings = configFile.AppSettings.Settings;
					if ( settings[key] == null )
					{
						settings.Add(key, value);

					}
					else
					{
						settings[key].Value = value;
					}

					configFile.Save(ConfigurationSaveMode.Modified);
					ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
				}

			}catch(ConfigurationErrorsException e)
			{
				System.Diagnostics.Debug.WriteLine("Error while writing app settings: {0}", e.Message);
			}

			return true;
		}

		public static string ReadSetting(string key)
		{
			string result = "Not Found";
			try
			{
				var appSettings = ConfigurationManager.AppSettings;
				result = appSettings[key] ?? "Not Found";

			}catch(ConfigurationErrorsException e)
			{
				System.Diagnostics.Debug.WriteLine("Error reading setting \"{0}\": {1}", key, e.Message);
			}

			return result;
		}

		public static bool SettingExists( string key )
		{
			string result = DataAccess.AppSettingConfigurator.ReadSetting(key);

			return result.Equals("Not Found") ? false : true;
		}

	}
}
