using System;
using System.ComponentModel.DataAnnotations;

namespace MinecraftServerDiscoveryApi.Models
{
    public class Server
    {
        [Key, Required]
        public int Id { get; set; }
        public string IpAddress { get; set; }
        public ushort Port { get; set; }
        public string Motd { get; set; }
        public string Version { get; set; }
        public int CurrentAmountPlayers { get; set; }
        public int MaxAmountPlayers { get; set; }
        public DateTime LastSeenOnline { get; set; }

    }
}
