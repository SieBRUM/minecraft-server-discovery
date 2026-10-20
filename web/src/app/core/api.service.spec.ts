import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService, toParams } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('only sends filters that are set', () => {
    api
      .servers({ q: 'paper', online: true, country: '', minPlayers: undefined, page: 2 })
      .subscribe();
    const req = http.expectOne((r) => r.url === '/api/servers');
    expect(req.request.params.keys().sort()).toEqual(['online', 'page', 'q']);
    expect(req.request.params.get('online')).toBe('true');
    req.flush({ items: [], total: 0, page: 2, pageSize: 25 });
  });

  it('encodes ids in paths', () => {
    expect(api.faviconUrl(42)).toBe('/api/servers/42/favicon');
    api.player(7).subscribe();
    http.expectOne('/api/players/7').flush({});
  });

  it('toParams drops NaN and null', () => {
    const params = toParams({ a: NaN, b: null, c: 0, d: false });
    expect(params.keys().sort()).toEqual(['c', 'd']);
  });
});
