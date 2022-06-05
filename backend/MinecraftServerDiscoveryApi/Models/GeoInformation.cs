using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftServerDiscoveryApi.Models
{
    public class GeoInformation
    {
        [Required, Key]
        public int Id { get; set; }
        public string Continent { get; set; }
        public string Country { get; set; }
        public string RegionName { get; set; }
        public string ZipCode { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Isp { get; set; }
        public string Timezone { get; set; }
    }
}
