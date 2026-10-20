using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace McDiscovery.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Favicons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Favicons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Uuid = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Engine = table.Column<string>(type: "TEXT", nullable: false),
                    TargetCount = table.Column<long>(type: "INTEGER", nullable: false),
                    OpenPorts = table.Column<long>(type: "INTEGER", nullable: false),
                    ServersOnline = table.Column<long>(type: "INTEGER", nullable: false),
                    NewServers = table.Column<long>(type: "INTEGER", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Servers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Address = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Edition = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastScannedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    VersionName = table.Column<string>(type: "TEXT", nullable: true),
                    ProtocolVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    MotdPlain = table.Column<string>(type: "TEXT", nullable: true),
                    MotdSegmentsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PlayersOnline = table.Column<int>(type: "INTEGER", nullable: true),
                    PlayersMax = table.Column<int>(type: "INTEGER", nullable: true),
                    PeakPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: true),
                    Software = table.Column<string>(type: "TEXT", nullable: true),
                    ModLoader = table.Column<string>(type: "TEXT", nullable: true),
                    ModsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ModCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EnforcesSecureChat = table.Column<bool>(type: "INTEGER", nullable: true),
                    PreviewsChat = table.Column<bool>(type: "INTEGER", nullable: true),
                    PreventsChatReports = table.Column<bool>(type: "INTEGER", nullable: true),
                    FaviconId = table.Column<long>(type: "INTEGER", nullable: true),
                    LoginResult = table.Column<int>(type: "INTEGER", nullable: false),
                    OnlineMode = table.Column<bool>(type: "INTEGER", nullable: true),
                    Whitelisted = table.Column<bool>(type: "INTEGER", nullable: true),
                    LoginMessage = table.Column<string>(type: "TEXT", nullable: true),
                    QueryEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    QueryGameType = table.Column<string>(type: "TEXT", nullable: true),
                    QueryMap = table.Column<string>(type: "TEXT", nullable: true),
                    QuerySoftware = table.Column<string>(type: "TEXT", nullable: true),
                    PluginsJson = table.Column<string>(type: "TEXT", nullable: true),
                    BedrockLevelName = table.Column<string>(type: "TEXT", nullable: true),
                    BedrockGameMode = table.Column<string>(type: "TEXT", nullable: true),
                    BedrockServerGuid = table.Column<string>(type: "TEXT", nullable: true),
                    ContinentCode = table.Column<string>(type: "TEXT", nullable: true),
                    CountryCode = table.Column<string>(type: "TEXT", nullable: true),
                    CountryName = table.Column<string>(type: "TEXT", nullable: true),
                    Region = table.Column<string>(type: "TEXT", nullable: true),
                    City = table.Column<string>(type: "TEXT", nullable: true),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Asn = table.Column<long>(type: "INTEGER", nullable: true),
                    AsOrganization = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Servers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Servers_Favicons_FaviconId",
                        column: x => x.FaviconId,
                        principalTable: "Favicons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ServerPlayers",
                columns: table => new
                {
                    ServerId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayerId = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TimesSeen = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerPlayers", x => new { x.ServerId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_ServerPlayers_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerPlayers_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerId = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsOnline = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlayersOnline = table.Column<int>(type: "INTEGER", nullable: true),
                    LatencyMs = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Snapshots_Servers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "Servers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Favicons_Sha256",
                table: "Favicons",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name",
                table: "Players",
                column: "Name",
                unique: true,
                filter: "\"Uuid\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Uuid",
                table: "Players",
                column: "Uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScanRuns_StartedUtc",
                table: "ScanRuns",
                column: "StartedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayers_PlayerId",
                table: "ServerPlayers",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_Address_Port_Edition",
                table: "Servers",
                columns: new[] { "Address", "Port", "Edition" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Servers_CountryCode",
                table: "Servers",
                column: "CountryCode");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_FaviconId",
                table: "Servers",
                column: "FaviconId");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_FirstSeenUtc",
                table: "Servers",
                column: "FirstSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_IsOnline",
                table: "Servers",
                column: "IsOnline");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_LastSeenUtc",
                table: "Servers",
                column: "LastSeenUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_PlayersOnline",
                table: "Servers",
                column: "PlayersOnline");

            migrationBuilder.CreateIndex(
                name: "IX_Servers_VersionName",
                table: "Servers",
                column: "VersionName");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_ServerId_TimestampUtc",
                table: "Snapshots",
                columns: new[] { "ServerId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_TimestampUtc",
                table: "Snapshots",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanRuns");

            migrationBuilder.DropTable(
                name: "ServerPlayers");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Servers");

            migrationBuilder.DropTable(
                name: "Favicons");
        }
    }
}
