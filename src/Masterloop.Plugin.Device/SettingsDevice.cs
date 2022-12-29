using Masterloop.Core.Types.Settings;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace Masterloop.Plugin.Device
{
    public class SettingsDevice : DeviceBase
    {
        #region Constants
        private const string _addressExpandedDeviceSettings = "/api/devices/{0}/settings/expanded";
        #endregion // Constants

        #region Properties
        public ExpandedSettingsPackage Settings { get; set; }
        #endregion

        #region Construction
        /// <summary>
        /// Constructs a new SettingsDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        /// <param name="port">Optional overriding of port number for HTTP(S) communication.</param>
        public SettingsDevice(string MID, String preSharedKey, String hostName, bool useHttps = true, ushort? port = null)
            : base(MID, preSharedKey, hostName, useHttps, port)
        {
        }
        #endregion

        #region Operations
        /// <summary>
        /// Download current settings for a device from cloud service.
        /// </summary>
        /// <returns>True if successful, False otherwise.</returns>
        public bool DownloadSettings()
        {
            string url = string.Format(_addressExpandedDeviceSettings, _MID);
            string result;
            if (Get(url, out result))
            {
                if (result != null && result.Length > 0)
                {
                    Settings = JsonConvert.DeserializeObject<ExpandedSettingsPackage>(result);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Retrieve a setting value based on setting id.
        /// </summary>
        /// <param name="id">Setting identifier.</param>
        /// <returns>ExpandedSettingValue of setting or </returns>
        public ExpandedSettingValue GetSettingValue(int id)
        {
            if (Settings != null)
            {
                return Settings.Values.Where(v => v.Id == id).First();
            }
            else
            {
                throw new Exception("Settings not initialized. Remember to call DownloadSettings before reading setting values.");
            }
        }
        #endregion
    }
}
