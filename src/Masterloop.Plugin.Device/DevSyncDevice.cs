using Masterloop.Core.Types.DevSync;
using Newtonsoft.Json;
using System;

namespace Masterloop.Plugin.Device
{
    public class DevSyncDevice : DeviceBase
    {
        #region Constants
        private const string _addressDevSync = "/api/devices/{0}/devsync";
        #endregion // Constants

        #region Construction
        /// <summary>
        /// Constructs a new DevSyncDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        public DevSyncDevice(string MID, String preSharedKey, String hostName, bool useHttps = true)
            : base(MID, preSharedKey, hostName, useHttps)
        {
        }
        #endregion

        #region Operations
        /// <summary>
        /// Logs an observation package containing identified observations on server.
        /// </summary>
        /// <param name="request">Device sync request of type DevSyncRequest.</param>
        /// <returns>DevSyncResponse object.</returns>
        public DevSyncResponse Sync(DevSyncRequest request)
        {
            string body = JsonConvert.SerializeObject(request);
            string url = string.Format(_addressDevSync, _MID);
            string response = PostMessageWithResponse(url, body);
            if (response != null)
            {
                return JsonConvert.DeserializeObject<DevSyncResponse>(response);
            }
            else
            {
                return null;
            }
        }
        #endregion
    }
}
