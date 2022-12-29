using System;
using Microsoft.Extensions.Configuration;

namespace Masterloop.Plugin.Device.Tests
{
    public abstract class DeviceTestBase
    {
        protected static IConfiguration _config;

        protected static IConfiguration GetConfig()
        {
            if (_config == null)
            {
                _config = new ConfigurationBuilder().AddJsonFile("appsettings.test.json").Build();
            }
            return _config;
        }

        protected static LiveDevice GetLiveDevice()
        {
            IConfiguration config = GetConfig();
            return new LiveDevice(config["MID"], config["PSK"], config["Hostname"], Boolean.Parse(config["UseHTTPS"]), ushort.Parse(config["Port"]));
        }

        protected static string GetMID()
        {
            IConfiguration config = GetConfig();
            return config["MID"];
        }

        protected static string GetPSK()
        {
            IConfiguration config = GetConfig();
            return config["PSK"];
        }
    }
}