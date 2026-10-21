/**
 * A deterministic 8x8 symmetric pixel avatar for a player name, as an SVG data URI. Generated locally from
 * a hash of the name: no skin downloads, nothing sent anywhere. The name never ends up in the SVG.
 */
export function pixelAvatar(name: string): string {
  let h = 2166136261;
  for (let i = 0; i < name.length; i++) {
    h = Math.imul(h ^ name.charCodeAt(i), 16777619) >>> 0;
  }
  const hue = h % 360;
  const fg = `hsl(${hue} 55% 50%)`;
  const bg = `hsl(${(hue + 180) % 360} 30% 88%)`;
  let rects = '';
  let bits = h;
  for (let y = 0; y < 8; y++) {
    for (let x = 0; x < 4; x++) {
      bits = Math.imul(bits ^ (bits >>> 15), 2246822519) >>> 0;
      if (bits & 1) {
        rects += `<rect x="${x}" y="${y}" width="1" height="1"/><rect x="${7 - x}" y="${y}" width="1" height="1"/>`;
      }
    }
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 8 8" shape-rendering="crispEdges"><rect width="8" height="8" fill="${bg}"/><g fill="${fg}">${rects}</g></svg>`;
  return 'data:image/svg+xml;base64,' + btoa(svg);
}
