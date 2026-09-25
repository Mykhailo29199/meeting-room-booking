import { InjectionToken } from '@angular/core';
import { environment } from '../../../environments/environment';

/**
 * Prefix for every API and hub URL (no trailing slash). Empty means the API is
 * on the client's own origin — in development via the dev-server proxy.
 * A token rather than a direct import so tests can point it elsewhere.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => environment.apiBaseUrl,
});
