using Masterloop.Core.Types.Settings;
using System;
using System.Linq;
using System.Collections.Generic;
using Masterloop.Core.Types.Base;
using LiteDB;

namespace Masterloop.Plugin.Device
{
    public class BufferedSetting
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int DataType { get; set; }
        public bool IsDefaultValue { get; set; }
        public string Json { get; set; }
    }

    public class BufferedSettingsDevice : SettingsDevice
    {
        #region PrivateMembers
        const string _SETBUF_NAME = "Settings";
        string _liteDBPathName;
        #endregion

        #region Construction
        /// <summary>
        /// Constructs a new BufferedSettingsDevice object.
        /// </summary>
        /// <param name="MID">Device identifier.</param>
        /// <param name="preSharedKey">Pre shared key for device.</param>
        /// <param name="hostName">Host to connect to, e.g. "myserver.example.com" or "10.0.0.2".</param>
        /// <param name="bufferPathName">Path and filename of buffer file. This process must be able to read/write to this file.</param>
        /// <param name="useHttps">True if using HTTPS (SSL/TLS), False if using HTTP (unencrypted).</param>
        public BufferedSettingsDevice(string MID, String preSharedKey, String hostName, string bufferPathName, bool useHttps = true)
            : base(MID, preSharedKey, hostName, useHttps)
        {
            this._liteDBPathName = bufferPathName;
        }
        #endregion

        #region Operations
        /// <summary>
        /// Download current settings for a device from cloud service, or from local buffer if cloud service is unavailable.
        /// </summary>
        /// <returns>True if successful, False otherwise.</returns>
        public new bool DownloadSettings()
        {
            bool wasDownloaded = base.DownloadSettings();
            if (wasDownloaded)
            {
                UpdateBufferedSettings(this.Settings);
            }
            else
            {
                this.Settings = GetBufferedSettings();
            }
            return this.Settings != null;
        }

        /// <summary>
        /// Loads settings from local buffer.
        /// </summary>
        public void LoadBufferedSettings()
        {
            Settings = GetBufferedSettings();
        }
        #endregion

        #region Private Methods
        private bool UpdateBufferedSettings(ExpandedSettingsPackage package)
        {
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                db.DropCollection(_SETBUF_NAME);

                var col = db.GetCollection<BufferedSetting>(_SETBUF_NAME);

                // Insert new setting values
                foreach (ExpandedSettingValue esv in package.Values)
                {
                    BufferedSetting bs = new BufferedSetting()
                    {
                        Name = esv.Name,
                        DataType = (int) esv.DataType,
                        IsDefaultValue = esv.IsDefaultValue,
                        Json = esv.Value
                    };
                    col.Insert(bs);
                }
                return true;
            }
        }

        private ExpandedSettingsPackage GetBufferedSettings()
        {
            ExpandedSettingsPackage package = new ExpandedSettingsPackage()
            {
                MID = _MID,
                LastUpdatedOn = DateTime.UtcNow
            };
            List<ExpandedSettingValue> settings = new List<ExpandedSettingValue>();
            using (LiteDatabase db = new LiteDatabase(_liteDBPathName))
            {
                var col = db.GetCollection<BufferedSetting>(_SETBUF_NAME);
                IEnumerable<BufferedSetting> storedSettings = col.FindAll().OrderBy(c => c.Id);
                foreach (BufferedSetting setting in storedSettings)
                {
                    ExpandedSettingValue esv = new ExpandedSettingValue();
                    esv.Id = setting.Id;
                    esv.Name = setting.Name;
                    esv.DataType = (DataType)setting.DataType;
                    esv.IsDefaultValue = setting.IsDefaultValue;
                    esv.Value = setting.Json;
                    settings.Add(esv);
                }
                package.Values = settings.ToArray();
            }
            return package;
        }
        #endregion
    }
}
