import { IServer } from "./server.model";

export interface IPlayer {
    id: number;
    playerName: string;
    servers: IServer[];
}