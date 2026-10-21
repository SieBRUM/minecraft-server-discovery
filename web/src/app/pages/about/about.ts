import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'app-about',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="panel">
      <h1>About</h1>
      <p>
        Minecraft Server Discovery is a research tool. A scanner finds Minecraft servers in IP
        ranges, asks them for the information they publish to every client (the server list ping,
        and optionally the query protocol and a login attempt) and stores it. This website only
        reads that data through the API.
      </p>
      <p>
        Everything shown here was reported by the servers themselves. Locations come from an offline
        GeoIP database and are approximate.
      </p>

      <h2>Credits</h2>
      <ul>
        <li>
          <a href="https://db-ip.com" target="_blank" rel="noopener noreferrer"
            >IP Geolocation by DB-IP</a
          >
          (DB-IP Lite, licensed under
          <a
            href="https://creativecommons.org/licenses/by/4.0/"
            target="_blank"
            rel="noopener noreferrer"
            >CC BY 4.0</a
          >)
        </li>
        <li>
          Map data &copy;
          <a
            href="https://www.openstreetmap.org/copyright"
            target="_blank"
            rel="noopener noreferrer"
            >OpenStreetMap</a
          >
          contributors, country outlines from
          <a href="https://www.naturalearthdata.com" target="_blank" rel="noopener noreferrer"
            >Natural Earth</a
          >
          (public domain) via
          <a
            href="https://github.com/topojson/world-atlas"
            target="_blank"
            rel="noopener noreferrer"
            >world-atlas</a
          >, map rendering by
          <a href="https://leafletjs.com" target="_blank" rel="noopener noreferrer">Leaflet</a>
        </li>
        <li>
          Port scanning by
          <a
            href="https://github.com/robertdavidgraham/masscan"
            target="_blank"
            rel="noopener noreferrer"
            >masscan</a
          >
          (Robert David Graham)
        </li>
        <li>
          Protocol documentation from the
          <a
            href="https://minecraft.wiki/w/Java_Edition_protocol"
            target="_blank"
            rel="noopener noreferrer"
            >Minecraft Wiki</a
          >
          (formerly wiki.vg); the original version of this project used
          <a href="https://github.com/FragLand/minestat" target="_blank" rel="noopener noreferrer"
            >MineStat</a
          >
        </li>
        <li>
          Charts by
          <a href="https://www.chartjs.org" target="_blank" rel="noopener noreferrer">Chart.js</a>,
          UI by Angular Material
        </li>
      </ul>
      <p>
        Source:
        <a
          href="https://github.com/SieBRUM/minecraft-server-discovery"
          target="_blank"
          rel="noopener noreferrer"
          >github.com/SieBRUM/minecraft-server-discovery</a
        >
        (Apache License 2.0). Not affiliated with Mojang or Microsoft.
      </p>
    </article>
  `,
  styles: `
    article {
      max-width: 760px;
      line-height: 1.6;
    }
  `,
})
export class About {}
