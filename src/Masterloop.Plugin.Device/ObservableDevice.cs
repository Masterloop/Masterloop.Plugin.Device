using System;
using Masterloop.Core.Types.Observations;

namespace Masterloop.Plugin.Device
{
    public class ObservableDevice : DeviceBase
    {
        #region Constants
        private const string _addressDeviceObservations = "/api/devices/{0}/observations";
        private const string _addressDeviceObservationsImport = "/api/devices/{0}/observations/import";
        #endregion // Constants

        #region Construction
        /// <summary>
        /// Constructs a new ObservableDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        /// <param name="port">Optional overriding of port number for HTTP(S) communication.</param>
        public ObservableDevice(string MID, String preSharedKey, String hostName, bool useHttps = true, ushort? port = null)
            : base(MID, preSharedKey, hostName, useHttps, port)
        {
            NotifyListenersOnUpload = true;
        }
        #endregion

        #region Properties
        public bool NotifyListenersOnUpload { get; set; }
        #endregion

        #region Operations
        /// <summary>
        /// Logs an observation package containing identified observations on server.
        /// </summary>
        /// <param name="package">Observation data of type ObservationPackage.</param>
        /// <param name="notifyListeners">True notifies any listeners on this device, False just transfers data to storage without any messaging.</param>
        /// <returns>True if uploading was successful, False otherwise.</returns>
        public bool LogObservations(ObservationPackage package)
        {
            CompactObservationPackage cop = new CompactObservationPackage(package);
            string body = cop.ToString();
            string url = string.Format(NotifyListenersOnUpload ? _addressDeviceObservations : _addressDeviceObservationsImport, _MID);
            return PostMessage(url, body, "text/csv");
        }
        #endregion
    }
}
