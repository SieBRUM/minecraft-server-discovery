import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ServerSummary } from '../../core/models';
import { ServerList } from './server-list';

const server: ServerSummary = {
  id: 1,
  address: '1.2.3.4',
  port: 25565,
  edition: 'Java',
  isOnline: true,
  firstSeenUtc: '2026-01-01T00:00:00Z',
  lastSeenUtc: '2026-01-02T00:00:00Z',
  versionName: 'Paper 1.21.4',
  protocolVersion: 769,
  motdPlain: 'Hello',
  motd: [
    {
      text: 'Hello',
      color: '#55ff55',
      bold: false,
      italic: false,
      underlined: false,
      strikethrough: false,
      obfuscated: false,
    },
  ],
  playersOnline: 3,
  playersMax: 20,
  latencyMs: 10,
  software: 'Paper',
  modLoader: null,
  onlineMode: false,
  whitelisted: null,
  countryCode: 'NL',
  countryName: 'Netherlands',
  city: 'Amsterdam',
  hasFavicon: false,
};

describe('ServerList', () => {
  it('loads and renders servers', async () => {
    TestBed.configureTestingModule({
      imports: [ServerList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ServerList);
    fixture.detectChanges();

    http.expectOne('/api/facets').flush({
      countries: [{ key: 'NL', label: 'Netherlands', count: 1 }],
      software: [],
      modLoaders: [],
    });
    http
      .expectOne((r) => r.url === '/api/servers')
      .flush({ items: [server], total: 1, page: 1, pageSize: 25 });
    await fixture.whenStable();

    const el = fixture.nativeElement as HTMLElement;
    const row = el.querySelector('tr.mat-mdc-row');
    expect(row?.textContent).toContain('1.2.3.4:25565');
    expect(row?.textContent).toContain('Hello');
    expect(row?.textContent).toContain('3 / 20');
    expect(row?.textContent).toContain('offline');
    http.verify();
  });
});
