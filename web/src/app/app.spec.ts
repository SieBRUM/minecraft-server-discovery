import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the navigation and the DB-IP attribution', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    const links = Array.from(el.querySelectorAll('nav a')).map((a) => a.textContent?.trim());
    expect(links).toEqual(['Servers', 'Map', 'Players', 'Stats', 'About']);
    const attribution = el.querySelector('footer a[href="https://db-ip.com"]');
    expect(attribution?.textContent).toContain('IP Geolocation by DB-IP');
    const author = el.querySelector('footer a[href="https://github.com/SieBRUM"]');
    expect(author?.textContent?.trim()).toBe('SieBRUM');
    expect(el.querySelector('footer')?.textContent).toContain('Made by SieBRUM');
  });
});
