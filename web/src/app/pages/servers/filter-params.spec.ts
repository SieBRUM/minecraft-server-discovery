import { convertToParamMap } from '@angular/router';
import { filterFromParams, filterToParams } from './filter-params';

describe('filter params', () => {
  it('round-trips a filter through the URL', () => {
    const filter = {
      q: 'survival',
      online: true,
      minPlayers: 5,
      dir: 'asc' as const,
      sort: 'players',
      page: 3,
    };
    expect(filterFromParams(convertToParamMap(filterToParams(filter)))).toEqual(filter);
  });

  it('ignores malformed values', () => {
    const filter = filterFromParams(
      convertToParamMap({
        online: 'yes',
        minPlayers: 'abc',
        dir: 'sideways',
        q: 'x'.repeat(500),
        unknown: '1',
      }),
    );
    expect(filter).toEqual({ q: 'x'.repeat(100) });
  });
});
