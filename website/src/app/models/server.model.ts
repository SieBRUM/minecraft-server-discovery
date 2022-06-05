import { IGeoInformation } from "./geo-information.model";
import { IPlayer } from "./player.model";

export interface IServer {
    id: number;
    ipAddress: string;
    port: number;
    motd: string;
    version: string;
    currentAmountPlayers: number;
    maxAmountPlayers: number;
    latency: number;
    firstDiscovered: Date;
    lastSeenOnline: Date;
    geoInformation: IGeoInformation;
    players: IPlayer[];
}