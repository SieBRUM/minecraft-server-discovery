namespace MinecraftServerDiscoveryApi.DTO
{
    public class ExternalGeoLocationInformation
    {
        public string Query { get; set; }
        public string Status { get; set; }
        public string Continent { get; set; }
        public string ContinentCode { get; set; }
        public string Country { get; set; }
        public string CountryCode { get; set; }
        public string Region { get; set; }
        public string RegionName { get; set; }
        public string City { get; set; }
        public string District { get; set; }
        public string Zip { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string Timezone { get; set; }
        public int Offset { get; set; }
        public string Currency { get; set; }
        public string Isp { get; set; }
        public string Org { get; set; }
        public string As { get; set; }
        public string Asname { get; set; }
        public bool Mobile { get; set; }
        public bool Proxy { get; set; }
        public bool Hosting { get; set; }
    }
}
