﻿using MinecraftServerDiscoveryApi.Models;
using MineStatLib;
using System;

namespace MinecraftServerDiscoveryApi.Helpers
{
    public class Converters
    {
        public static Server MineStatToNewServer(MineStat ms)
        {
            return new Server()
            {
                IpAddress = ms.Address,
                Port = ms.Port,
                Motd = ms.Motd,
                Version = ms.Version,
                CurrentAmountPlayers = ms.CurrentPlayersInt,
                MaxAmountPlayers = ms.MaximumPlayersInt,
                Latency = ms.Latency,
                FirstDiscovered = DateTime.Now,
                LastSeenOnline = DateTime.Now
            };
        }

        public static void MineStatToExistingServer(MineStat ms, ref Server server)
        {
            server.Motd = ms.Motd;
            server.Version = ms.Version;
            server.CurrentAmountPlayers = ms.CurrentPlayersInt;
            server.MaxAmountPlayers = ms.MaximumPlayersInt;
            server.Latency = ms.Latency;
            server.LastSeenOnline = DateTime.Now;
        }
    }
}
