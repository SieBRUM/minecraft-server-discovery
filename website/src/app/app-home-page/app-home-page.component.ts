import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';
import { IServer } from '../models/server.model';
import * as moment from 'moment';

@Component({
  selector: 'app-home-page',
  templateUrl: './app-home-page.component.html',
  styleUrls: ['./app-home-page.component.scss']
})
export class AppHomePageComponent implements OnInit {

  isLoading = false;
  servers: IServer[] = [];
  displayedColumns: string[] = ['ipport', 'motd', 'players', 'version', 'lastonline', 'geocountry', 'maps'];

  constructor(
    public apiService: ApiService
  ) { }

  ngOnInit(): void {
    this.isLoading = true;
    this.apiService.getAllServers().subscribe({
      next: (response) => {
        if (response != null) {
          this.servers = response.body as IServer[];
        }
        this.isLoading = false;
      },
      error: (err: any) => {
        this.isLoading = false;
      }
    });
  }

  onClickMap(lat: string, lon: string): void {
    window.open(`https://maps.google.com/?q=${lat},${lon}`, '_blank');
  }

  getDate(date: string): string {
    return moment(date).fromNow();
  }

  refreshInformation(id: number): void {
    var serverIndex = this.servers.findIndex(x => x.id == id);
    this.apiService.updateServer(this.servers[serverIndex].ipAddress).subscribe({
      next: (response) => {
        if (response != null) {
          var temp = JSON.parse(JSON.stringify(this.servers)) as IServer[];
          temp[serverIndex] = response.body as IServer;
          this.servers = temp;
        }
        this.isLoading = false;
      },
      error: (err: any) => {
        this.isLoading = false;
      }
    });
  }

  onClickSortPlayers(): void {
    this.servers = JSON.parse(JSON.stringify(this.servers.sort(this.sortByNumber)));
  }

  private sortByNumber(a: IServer, b: IServer): number {
    if (a.currentAmountPlayers < b.currentAmountPlayers) {
      return 1;
    }

    if (a.currentAmountPlayers > b.currentAmountPlayers) {
      return -1;
    }

    return 0;
  }
}
