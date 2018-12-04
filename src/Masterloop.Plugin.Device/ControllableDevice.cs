using System;
using Newtonsoft.Json;
using Masterloop.Core.Types;
using Masterloop.Core.Types.Commands;

namespace Masterloop.Plugin.Device
{
    public class ControllableDevice : DeviceBase
    {
        #region Constants
        private const string _addressDeviceCommandQueue = "/api/devices/{0}/commands/queue";
        private const string _addressDeviceCommandResponse = "/api/devices/{0}/commands/{1}/response";
        #endregion // Constants

        #region Construction
        /// <summary>
        /// Constructs a new ControllableDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        public ControllableDevice(string MID, String preSharedKey, String hostName, bool useHttps = true)
            : base(MID, preSharedKey, hostName, useHttps)
        {
        }
        #endregion

        #region Operations
        /// <summary>
        /// Get device command queue from server.
        /// </summary>
        /// <returns>Array of Command objects in queue, or null if queue is empty.</returns>
        public Command[] GetCommandQueue()
        {
            string url = string.Format(_addressDeviceCommandQueue, _MID);
            string result;
            if (Get(url, out result))
            {
                if (result != null)
                {
                    return JsonConvert.DeserializeObject<Command[]>(result);
                }
            }
            return null;
        }

        /// <summary>
        /// Sends command response to server.
        /// </summary>
        /// <param name="commandId">Command identifier.</param>
        /// <param name="commandResponse">Command response structure.</param>
        /// <returns>True if successful, False otherwise.</returns>
        public bool RespondToCommand(int commandId, CommandResponse commandResponse)
        {
            string body = JsonConvert.SerializeObject(commandResponse);
            string url = string.Format(_addressDeviceCommandResponse, _MID, commandId);
            return PostMessage(url, body);
        }
        #endregion
    }
}
