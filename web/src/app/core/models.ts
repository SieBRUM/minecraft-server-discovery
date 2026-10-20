// Shapes returned by the API (see src/McDiscovery.Api/Endpoints/Dtos.cs).

export type Edition = 'Java' | 'Bedrock';

export type LoginResult =
  | 'NotProbed'
  | 'OnlineMode'
  | 'OfflineMode'
  | 'Whitelisted'
  | 'Kicked'
  | 'PluginRequest'
  | 'Failed';

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface MotdSegment {
  text: string;
  color: string | null;
  bold: boolean;
  italic: boolean;
  underlined: boolean;
  strikethrough: boolean;
  obfuscated: boolean;
}

export interface ServerSummary {
  id: number;
  address: string;
  port: number;
  edition: Edition;
  isOnline: boolean;
  firstSeenUtc: string;
  lastSeenUtc: string;
  versionName: string | null;
  protocolVersion: number | null;
  motdPlain: string | null;
  motd: MotdSegment[];
  playersOnline: number | null;
  playersMax: number | null;
  latencyMs: number | null;
  software: string | null;
  modLoader: string | null;
  onlineMode: boolean | null;
  whitelisted: boolean | null;
  countryCode: string | null;
  countryName: string | null;
  city: string | null;
  hasFavicon: boolean;
}

export interface ServerPlayer {
  playerId: number;
  name: string;
  uuid: string | null;
  firstSeenUtc: string;
  lastSeenUtc: string;
  timesSeen: number;
}

export interface ServerDetail {
  summary: ServerSummary;
  lastScannedUtc: string;
  consecutiveFailures: number;
  peakPlayers: number;
  enforcesSecureChat: boolean | null;
  previewsChat: boolean | null;
  preventsChatReports: boolean | null;
  mods: { id: string; version: string | null }[];
  loginResult: LoginResult;
  loginMessage: string | null;
  queryEnabled: boolean;
  queryGameType: string | null;
  queryMap: string | null;
  querySoftware: string | null;
  plugins: string[];
  bedrockLevelName: string | null;
  bedrockGameMode: string | null;
  bedrockServerGuid: string | null;
  continentCode: string | null;
  region: string | null;
  latitude: number | null;
  longitude: number | null;
  asn: number | null;
  asOrganization: string | null;
  playerCount: number;
  players: ServerPlayer[];
}

export interface Snapshot {
  t: string;
  online: boolean;
  players: number | null;
  latencyMs: number | null;
}

export interface MapPoint {
  lat: number;
  lon: number;
  count: number;
  online: number;
  players: number;
}

export interface Player {
  id: number;
  name: string;
  uuid: string | null;
  firstSeenUtc: string;
  lastSeenUtc: string;
  serverCount: number;
}

export interface PlayerServer {
  serverId: number;
  address: string;
  port: number;
  edition: Edition;
  motdPlain: string | null;
  isOnline: boolean;
  firstSeenUtc: string;
  lastSeenUtc: string;
  timesSeen: number;
}

export interface PlayerDetail {
  player: Player;
  servers: PlayerServer[];
}

export interface Count {
  key: string;
  label: string | null;
  count: number;
}

export interface ScanRun {
  id: number;
  kind: 'Scan' | 'Rescan';
  startedUtc: string;
  finishedUtc: string | null;
  description: string;
  engine: string;
  targetCount: number;
  openPorts: number;
  serversOnline: number;
  newServers: number;
  completed: boolean;
}

export interface Stats {
  servers: number;
  online: number;
  java: number;
  bedrock: number;
  newLast24h: number;
  seenLast24h: number;
  playersOnline: number;
  knownPlayers: number;
  withQuery: number;
  modded: number;
  countries: Count[];
  versions: Count[];
  software: Count[];
  modLoaders: Count[];
  loginResults: Count[];
  asns: Count[];
  recentScans: ScanRun[];
  generatedUtc: string;
}

export interface Facets {
  countries: Count[];
  software: Count[];
  modLoaders: Count[];
}

/** Filters of the server list. Kept in the URL query string so views can be shared. */
export interface ServerFilter {
  q?: string;
  edition?: string;
  online?: boolean;
  country?: string;
  version?: string;
  software?: string;
  modLoader?: string;
  onlineMode?: boolean;
  whitelisted?: boolean;
  minPlayers?: number;
  modded?: boolean;
  hasPlugins?: boolean;
  cellLat?: number;
  cellLon?: number;
  sort?: string;
  dir?: 'asc' | 'desc';
  page?: number;
  pageSize?: number;
}
