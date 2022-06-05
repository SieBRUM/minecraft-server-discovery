using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinecraftServerDiscoveryApi.Contexts;
using MinecraftServerDiscoveryApi.Helpers;
using MinecraftServerDiscoveryApi.Models;
using MineStatLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MinecraftServerDiscoveryApi.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MainController : ControllerBase
    {
        private readonly DatabaseContext _dbContext;
        private readonly Regex ipRegex = new Regex(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b");

        public MainController(DatabaseContext context)
        {
            _dbContext = context;
        }

        [HttpGet("submit/{ip}")]
        public async Task<IActionResult> DiscoverServer(string ip)
        {
            if(string.IsNullOrWhiteSpace(ip))
            {
                return BadRequest();
            }

            if(!ipRegex.IsMatch(ip))
            {
                return BadRequest();
            }

            var minecraftServer = await _dbContext.Servers.Include(x => x.Players).Include(x => x.GeoInformation).FirstOrDefaultAsync(x => x.IpAddress == ip);
            var ms = new MineStat(ip, 25565);

            if(ms.ServerUp)
            {
                // New Up Server found
                if(minecraftServer == default)
                {
                    minecraftServer = Converters.MineStatToNewServer(ms);
                    await GeoLocationHelper.SetGeoData(minecraftServer);
                    await _dbContext.GeoInformation.AddAsync(minecraftServer.GeoInformation);
                    await _dbContext.SaveChangesAsync();
                    await _dbContext.Servers.AddAsync(minecraftServer);
                    await _dbContext.SaveChangesAsync();
                }
                // Already existing server found
                else
                {
                    Converters.MineStatToExistingServer(ms, ref minecraftServer);
                    if(minecraftServer.GeoInformation == default)
                    {
                        await _dbContext.GeoInformation.AddAsync(minecraftServer.GeoInformation);
                    }
                    await _dbContext.SaveChangesAsync();
                }

                if(ms.CurrentPlayersInt != 0 && ms.CurrentPlayers != default)
                {
                    foreach (var player in ms.PlayerList)
                    {
                        var dbPlayer = await _dbContext.Players.FirstOrDefaultAsync(x => x.PlayerName == player);
                        if (player == default)
                        {
                            dbPlayer = new Player()
                            {
                                PlayerName = player,
                                Servers = new List<Server>()
                            };
                            dbPlayer.Servers.Add(minecraftServer);
                            await _dbContext.Players.AddAsync(dbPlayer);
                            await _dbContext.SaveChangesAsync();
                        }
                        else
                        {
                            if (!dbPlayer.Servers.Any(x => x.IpAddress == ip))
                            {
                                dbPlayer.Servers.Add(minecraftServer);
                                await _dbContext.SaveChangesAsync();
                            }
                        }
                    }
                }
            }
            else
            {
                if(minecraftServer == default)
                {
                    minecraftServer = new Server()
                    {
                        Port = 25565,
                        IpAddress = ip
                    };

                    await _dbContext.AddAsync(minecraftServer);
                    await _dbContext.SaveChangesAsync();
                }
            }

            return Ok(minecraftServer);
        }

        [HttpGet("server/{ip}")]
        public async Task<IActionResult> GetServerInfo(string ip)
        {

            if (string.IsNullOrWhiteSpace(ip))
            {
                return BadRequest();
            }

            if (!ipRegex.IsMatch(ip))
            {
                return BadRequest();
            }

            var server = _dbContext.Servers.FirstOrDefaultAsync(x => x.IpAddress == ip);

            if(server == default)
            {
                return NotFound();
            }

            return Ok(server);
        }

        [HttpGet("server")]
        public async Task<IActionResult> GetAllServers()
        {
            return Ok(await _dbContext.Servers.Include(x => x.Players).Include(x => x.GeoInformation).ToListAsync());
        }
    }
}
