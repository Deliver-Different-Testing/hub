/** Full-page navigation helpers. Wrapped so tests can replace them (jsdom cannot navigate). */
export const navigateTo = (url: string): void => window.location.assign(url);
export const reloadPage = (): void => window.location.reload();
