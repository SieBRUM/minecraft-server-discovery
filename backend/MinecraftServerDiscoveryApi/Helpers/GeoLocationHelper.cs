using MinecraftServerDiscoveryApi.Models;
using Flurl.Http;
using System.Threading.Tasks;
using MinecraftServerDiscoveryApi.DTO;
using System;

namespace MinecraftServerDiscoveryApi.Helpers
{
    public class GeoLocationHelper
    {
        public async static Task SetGeoData(Server server)
        {
            try
            {
                var extGeoInfo = await $"http://ip-api.com/json/{server.IpAddress}".AllowAnyHttpStatus().GetJsonAsync<ExternalGeoLocationInformation>();
                server.GeoInformation = Converters.ExternalGeoInformationToNewGeoInformation(extGeoInfo);
            }
            catch (Exception)
            {
                // Ratelimit by geoInfo. 
            }
        }
    }
}
