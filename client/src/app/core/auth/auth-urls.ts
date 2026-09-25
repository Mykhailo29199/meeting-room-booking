/** Where signed-out users are sent. */
export const LOGIN_URL = '/login';

/** Where users land after signing in when there is nowhere to return to. */
export const HOME_URL = '/resources';

/**
 * The `returnUrl` query parameter if it is a path inside this app, otherwise
 * {@link HOME_URL}. Anything else (`https://evil.example`, `//evil.example`)
 * would turn the login page into an open redirect.
 */
export function safeReturnUrl(returnUrl: string | null | undefined): string {
  if (
    !returnUrl ||
    !returnUrl.startsWith('/') ||
    returnUrl.startsWith('//') ||
    returnUrl.startsWith('/\\') ||
    returnUrl.startsWith(LOGIN_URL)
  ) {
    return HOME_URL;
  }
  return returnUrl;
}
