using System;
using Newtonsoft.Json;
using System.Globalization;
using Masterloop.Core.Types.EventLog;

namespace Masterloop.Plugin.Device
{
    public class DeviceBase : IDisposable
    {
        #region Constants
        private const string _addressToolsPing = "/api/tools/ping";
        private const string _addressServerTime = "/api/tools/servertime";
        private const string _addressDeviceEvents = "/api/devices/{0}/events";
        private const int _defaultTimeout = 30; // 30 seconds
        #endregion // Constants

        #region ProtectedMembers
        protected readonly bool _useHttps;
        protected readonly string _MID;
        protected readonly string _preSharedKey;
        protected readonly string _hostName;
        protected readonly string _baseAddress;
        protected string _lastErrorMessage;
        #endregion

        #region Properties
        /// <summary>
        /// Device identifier.
        /// </summary>
        public string MID
        {
            get
            {
                return _MID;
            }
        }

        /// <summary>
        /// Last error message after an error, or null if no errors have occured.
        /// </summary>
        public string LastErrorMessage
        {
            get
            {
                return _lastErrorMessage;
            }
            protected set
            {
                _lastErrorMessage = value;
            }
        }

        /// <summary>
        /// Network timeout in seconds. Default 30 seconds.
        /// </summary>
        public int Timeout { get; set; }
        #endregion  // Properties

        #region Construction
        /// <summary>
        /// Constructs a new BasicDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        /// <param name="port">Optional overriding of port number for HTTP(S) communication.</param>
        public DeviceBase(string MID, String preSharedKey, String hostName, bool useHttps = true, ushort? port = null)
        {
            _hostName = hostName;
            _useHttps = useHttps;
            _MID = MID;
            _preSharedKey = preSharedKey;
            if (useHttps)
            {
                _baseAddress = string.Format("https://{0}", hostName);
            }
            else
            {
                _baseAddress = string.Format("http://{0}", hostName);
            }

            // Append port number if overridden.
            if (port.HasValue)
            {
                _baseAddress += $":{port.Value}";
            }
            Timeout = _defaultTimeout;
        }

        /// <summary>
        /// Destroys this object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
            }
        }
        #endregion

        #region CommonOperations
        /// <summary>
        /// Check if server is reachable.
        /// </summary>
        /// <returns>True if reachable, False otherwise.</returns>
        public bool CanPingServer()
        {
            string received;
            if (Get(_addressToolsPing, out received))
            {
                return received.Contains("PONG");
            }
            return false;
        }

        /// <summary>
        /// Get server time.
        /// </summary>
        /// <returns>Server time at the time of handling of the request.</returns>
        public DateTime GetServerTime()
        {
            string received;
            if (Get(_addressServerTime, out received))
            {
                DateTime serverTime;
                if (DateTime.TryParse(received, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out serverTime))
                {
                    return serverTime;
                }
            }
            throw new Exception("Failed to obtain server timestamp.");
        }

        /// <summary>
        /// Report event to server.
        /// </summary>
        /// <param name="reportedDeviceEvent">Device event of type ReportedDeviceEvent.</param>
        /// <returns>True if reporting was successful, False otherwise.</returns>
        public bool ReportDeviceEvent(ReportedDeviceEvent reportedDeviceEvent)
        {
            string body = JsonConvert.SerializeObject(reportedDeviceEvent);
            string url = string.Format(_addressDeviceEvents, _MID);
            return PostMessage(url, body);
        }
        #endregion

        #region ProtectedMethods
        protected bool Get(string addressExtension, out string result)
        {
            ExtendedWebClient webClient = new ExtendedWebClient();
            webClient.Accept = "application/json";
            webClient.Username = _MID ;
            webClient.Password = _preSharedKey;
            webClient.Timeout = Timeout;
            string url = _baseAddress + addressExtension;
            try
            {
                result = webClient.DownloadString(url);
                if (webClient.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
                else
                {
                    result = string.Empty;
                    LastErrorMessage = webClient.StatusDescription;
                    return false;
                }
            }
            catch (System.Exception error)
            {
                _lastErrorMessage = error.ToString();
                result = string.Empty;
                return false;
            }
        }

        protected bool Get(string addressExtension, out byte[] result)
        {
            ExtendedWebClient webClient = new ExtendedWebClient();
            webClient.Accept = "application/json";
            webClient.Username = _MID;
            webClient.Password = _preSharedKey;
            webClient.Timeout = Timeout;
            string url = _baseAddress + addressExtension;
            try
            {
                result = webClient.DownloadBytes(url);
                if (webClient.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
                else
                {
                    result = null;
                    LastErrorMessage = webClient.StatusDescription;
                    return false;
                }
            }
            catch (System.Exception error)
            {
                _lastErrorMessage = error.ToString();
                result = null;
                return false;
            }
        }

        protected bool PostMessage(string addressExtension, string body, string contentType = "application/json")
        {
            ExtendedWebClient webClient = new ExtendedWebClient();
            webClient.ContentType = contentType;
            webClient.Accept = "application/json";
            webClient.Username = _MID;
            webClient.Password = _preSharedKey;
            webClient.Timeout = Timeout;
            string url = _baseAddress + addressExtension;
            try
            {
                webClient.UploadString(url, body);
                if (webClient.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return true;
                }
                else
                {
                    _lastErrorMessage = webClient.StatusDescription;
                    return false;
                }
            }
            catch (System.Exception error)
            {
                _lastErrorMessage = error.ToString();
                return false;
            }
        }        

        protected string PostMessageWithResponse(string addressExtension, string body, string contentType = "application/json")
        {
            ExtendedWebClient webClient = new ExtendedWebClient();
            webClient.ContentType = contentType;
            webClient.Accept = "application/json";
            webClient.Username = _MID;
            webClient.Password = _preSharedKey;
            webClient.Timeout = Timeout;
            string url = _baseAddress + addressExtension;
            try
            {
                string result = webClient.UploadString(url, body);
                if (webClient.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return result;
                }
                else
                {
                    _lastErrorMessage = webClient.StatusDescription;
                    return null;
                }
            }
            catch (System.Exception error)
            {
                _lastErrorMessage = error.ToString();
                return null;
            }
        }
        #endregion
    }
}
