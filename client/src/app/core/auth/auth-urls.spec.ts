import { safeReturnUrl } from './auth-urls';

describe('safeReturnUrl', () => {
  it('returns to a page of this app', () => {
    expect(safeReturnUrl('/resources/42?date=2026-10-01')).toBe('/resources/42?date=2026-10-01');
  });

  it.each([
    [null],
    [''],
    ['https://evil.example/'],
    ['//evil.example/'],
    ['/\\evil.example/'],
    ['javascript:alert(1)'],
    ['/login?returnUrl=%2Fresources'],
  ])('goes home instead of %s', (returnUrl) => {
    expect(safeReturnUrl(returnUrl)).toBe('/resources');
  });
});
