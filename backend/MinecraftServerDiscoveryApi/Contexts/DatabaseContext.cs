using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MinecraftServerDiscoveryApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MinecraftServerDiscoveryApi.Contexts
{
    public class DatabaseContext : DbContext
    {

        public DbSet<GeoInformation> GeoInformation { get; set; }
        public DbSet<Player> Players { get; set; }
        public DbSet<Server> Servers { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbString = Environment.GetEnvironmentVariable("MINECRAFT_DISCOVERY_DB");
            if (string.IsNullOrWhiteSpace(dbString))
            {
                throw new MissingFieldException("Database environment variable not found.");
            }

            optionsBuilder.UseMySql(dbString, ServerVersion.AutoDetect(dbString));
            optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();

            base.OnConfiguring(optionsBuilder);
        }
    }
}
