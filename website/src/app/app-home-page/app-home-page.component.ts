import { Component, OnInit } from '@angular/core';
import { ApiService } from '../api.service';
import { IServer } from '../models/server.model';

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
}
