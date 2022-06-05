using MinecraftServerDiscoveryApi.Models;
using Flurl.Http;
using System.Threading.Tasks;
using MinecraftServerDiscoveryApi.DTO;

namespace MinecraftServerDiscoveryApi.Helpers
{
    public class GeoLocationHelper
    {
        public async static Task SetGeoData(Server server)
        {
            var extGeoInfo = await $"http://ip-api.com/json/{server.IpAddress}".AllowAnyHttpStatus().GetJsonAsync<ExternalGeoLocationInformation>();

            server.GeoInformation = Converters.ExternalGeoInformationToNewGeoInformation(extGeoInfo);
        }
    }
}
