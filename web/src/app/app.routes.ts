import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'servers' },
  {
    path: 'servers',
    title: 'Servers',
    loadComponent: () => import('./pages/servers/server-list').then((m) => m.ServerList),
  },
  {
    path: 'servers/:id',
    title: 'Server',
    loadComponent: () => import('./pages/server-detail/server-detail').then((m) => m.ServerDetail),
  },
  {
    path: 'map',
    title: 'Map',
    loadComponent: () => import('./pages/map/map-page').then((m) => m.MapPage),
  },
  {
    path: 'players',
    title: 'Players',
    loadComponent: () => import('./pages/players/player-list').then((m) => m.PlayerList),
  },
  {
    path: 'players/:id',
    title: 'Player',
    loadComponent: () => import('./pages/players/player-detail').then((m) => m.PlayerDetail),
  },
  {
    path: 'stats',
    title: 'Statistics',
    loadComponent: () => import('./pages/stats/stats-page').then((m) => m.StatsPage),
  },
  {
    path: 'about',
    title: 'About',
    loadComponent: () => import('./pages/about/about').then((m) => m.About),
  },
  { path: '**', redirectTo: 'servers' },
];
