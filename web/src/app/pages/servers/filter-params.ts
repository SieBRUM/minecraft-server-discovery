import { ParamMap, Params } from '@angular/router';
import { ServerFilter } from '../../core/models';

const BOOLEAN_KEYS = ['online', 'onlineMode', 'whitelisted', 'modded', 'hasPlugins'] as const;
const NUMBER_KEYS = ['minPlayers', 'cellLat', 'cellLon', 'page', 'pageSize'] as const;
const STRING_KEYS = [
  'q',
  'edition',
  'country',
  'version',
  'software',
  'modLoader',
  'sort',
] as const;

/** Reads the server list filter from the URL, ignoring anything malformed. */
export function filterFromParams(params: ParamMap): ServerFilter {
  const filter: ServerFilter = {};
  for (const key of STRING_KEYS) {
    const value = params.get(key);
    if (value) {
      filter[key] = value.slice(0, 100);
    }
  }
  for (const key of BOOLEAN_KEYS) {
    const value = params.get(key);
    if (value === 'true' || value === 'false') {
      filter[key] = value === 'true';
    }
  }
  for (const key of NUMBER_KEYS) {
    const value = Number(params.get(key) ?? NaN);
    if (params.has(key) && Number.isFinite(value)) {
      filter[key] = value;
    }
  }
  const dir = params.get('dir');
  if (dir === 'asc' || dir === 'desc') {
    filter.dir = dir;
  }
  return filter;
}

/** The inverse of {@link filterFromParams}; empty values are left out of the URL. */
export function filterToParams(filter: ServerFilter): Params {
  const params: Params = {};
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== null && value !== '') {
      params[key] = String(value);
    }
  }
  return params;
}
