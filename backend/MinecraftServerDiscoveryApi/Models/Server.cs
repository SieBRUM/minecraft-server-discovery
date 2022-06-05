using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MinecraftServerDiscoveryApi.Models
{
    public class Server
    {
        [Key, Required]
        public int Id { get; set; }
        [Required]
        public string IpAddress { get; set; }
        [Required]
        public ushort Port { get; set; }
        public string Motd { get; set; }
        public string Version { get; set; }
        public int CurrentAmountPlayers { get; set; }
        public int MaxAmountPlayers { get; set; }
        public long Latency { get; set; }
        [Required]
        public DateTime FirstDiscovered { get; set; }
        [Required]
        public DateTime LastSeenOnline { get; set; }

        public GeoInformation GeoInformation { get; set; }
        public ICollection<Player> Players { get; set; } 
    }
}
