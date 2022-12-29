using Masterloop.Core.Types.Firmware;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace Masterloop.Plugin.Device
{
    public class FirmwareDevice : DeviceBase
    {
        #region Constants
        private const string _addressDeviceFirmwareCurrent = "/api/devices/{0}/firmware/current";
        private const string _addressDeviceFirmwareCurrentData = "/api/devices/{0}/firmware/current/data";
        private const string _addressDeviceFirmwarePatch = "/api/devices/{0}/firmware/patch/{1}";
        #endregion // Constants

        #region Construction
        /// <summary>
        /// Constructs a new FirmwareDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        /// <param name="port">Optional overriding of port number for HTTP(S) communication.</param>
        public FirmwareDevice(string MID, String preSharedKey, String hostName, bool useHttps = true, ushort? port = null)
            : base(MID, preSharedKey, hostName, useHttps, port)
        {
        }
        #endregion

        #region Operations
        /// <summary>
        /// Get meta information about the current firmware release.
        /// </summary>
        /// <returns>Firmware release descriptor for the current firmware release.</returns>
        public FirmwareReleaseDescriptor GetFirmwareRelease()
        {
            string addressExtension = string.Format(_addressDeviceFirmwareCurrent, _MID);
            string result;
            if (Get(addressExtension, out result))
            {
                if (result != null)
                {
                    return JsonConvert.DeserializeObject<FirmwareReleaseDescriptor>(result);
                }
            }
            return null;
        }

        /// <summary>
        /// Download a complete firmware release.
        /// </summary>
        /// <param name="descriptor">Firmware release descriptor returned by GetFirmwareRelease.</param>
        /// <param name="validate">True to include checksum check (an exception will be thrown in case of mismatch), or False to skip checksum check.</param>
        /// <returns>Array of bytes with the current firmware release.</returns>
        public byte[] DownloadFirmwareRelease(FirmwareReleaseDescriptor descriptor, bool validate = true, bool useAuthentication = false)
        {
            WebClient client = new WebClient();
            byte[] data;

            if (useAuthentication)  // If authentication is required, stream through the API
            {
                string url = string.Format(_addressDeviceFirmwareCurrentData, _MID);
                if (!Get(url, out data))
                {
                    throw new InvalidDataException("Failed to download data from url: " + url);
                }
            }
            else
            {
                data = client.DownloadData(descriptor.Url);
            }

            if (validate)
            {
                MD5 md5 = MD5.Create();
                byte[] md5Hash = md5.ComputeHash(data);
                string md5String = Convert.ToBase64String(md5Hash);
                if (string.Compare(md5String, descriptor.FirmwareMD5) == 0)
                {
                    return data;
                }
                else
                {
                    throw new InvalidDataException("Checksum error detected in firmware release blob.");
                }
            }
            else
            {
                return data;
            }
        }

        /// <summary>
        /// Get meta information about firmware patch.
        /// </summary>
        /// <param name="fromVersionNo">Current version number of firmware on device.</param>
        /// <returns>Firmware patch descriptor for the firmware update patch, or null if fromVersionNo is the latest version.</returns>
        public FirmwarePatchDescriptor GetFirmwarePatch(int fromVersionNo)
        {
            string addressExtension = string.Format(_addressDeviceFirmwarePatch, _MID, fromVersionNo);
            string result;
            if (Get(addressExtension, out result))
            {
                return JsonConvert.DeserializeObject<FirmwarePatchDescriptor>(result);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Download a firmware patch.
        /// </summary>
        /// <param name="descriptor">Firmware patch descriptor returned by GetFirmwarePatch.</param>
        /// <param name="validate">True to include checksum check (an exception will be thrown in case of mismatch), or False to skip checksum check.</param>
        /// <returns>Array of bytes with the current firmware patch.</returns>
        public byte[] DownloadFirmwarePatch(FirmwarePatchDescriptor descriptor, bool validate = true)
        {
            WebClient client = new WebClient();
            byte[] data = client.DownloadData(descriptor.Url);

            if (validate)
            {
                MD5 md5 = MD5.Create();
                byte[] md5Hash = md5.ComputeHash(data);
                string md5String = Convert.ToBase64String(md5Hash);
                if (string.Compare(md5String, descriptor.PatchMD5) == 0)
                {
                    return data;
                }
                else
                {
                    throw new InvalidDataException("Checksum error detected in firmware patch blob.");
                }
            }
            else
            {
                return data;
            }
        }
        #endregion
    }
}
