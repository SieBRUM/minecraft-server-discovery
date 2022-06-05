using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftServerDiscoveryApi.Models
{
    public class Player
    {
        [Required, Key]
        public int Id { get; set; }
        [Required]
        public string PlayerName { get; set; }

        public ICollection<Server> Servers { get; set; }
    }
}
