import { http, HttpResponse } from 'msw';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { logoHandler, ME_NZ, ME_US, meHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import TenantLogo from './TenantLogo';

const logo = async () => screen.findByRole('img', { name: 'Deliver DFRNT' });

describe('TenantLogo', () => {
  it('shows the tenant logo from api/Logo', async () => {
    server.use(meHandler(ME_US), logoHandler('/images/DFRNT_HorizLogo_RGB.png'));
    renderWithProviders(<TenantLogo />);
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB.png');
  });

  it('uses the _dark variant of a local logo on dark surfaces (colour mode dark)', async () => {
    window.localStorage.setItem('hub-color-mode', 'dark');
    server.use(meHandler(ME_US), logoHandler('/images/DFRNT_HorizLogo_RGB.png'));
    renderWithProviders(<TenantLogo />);
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB_dark.png');
  });

  it('on the Ink shell a DFRNT-brand tenant gets the dark artwork even in light mode', async () => {
    server.use(meHandler(ME_US), logoHandler('/images/DFRNT_HorizLogo_RGB.png'));
    renderWithProviders(<TenantLogo onShell />);
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB_dark.png');
  });

  it('on a light-primary bar uses the standard artwork, on a dark-primary bar the reversed one', async () => {
    server.use(meHandler(ME_US), logoHandler('/images/DFRNT_HorizLogo_RGB.png'));
    const light = renderWithProviders(<TenantLogo onShell />, { appearance: { highContrast: false, primary: '#f4c430' } });
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB.png');
    light.unmount();

    renderWithProviders(<TenantLogo onShell />, { appearance: { highContrast: false, primary: '#132279' } });
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB_dark.png');
  });

  it('never derives a dark variant for tenant-uploaded (remote) logos and plates them on a light chip', async () => {
    window.localStorage.setItem('hub-color-mode', 'dark');
    server.use(meHandler(ME_US), logoHandler('https://bucket.example/logo.png?sig=ab'));
    renderWithProviders(<TenantLogo onShell />);
    const img = await logo();
    expect(img).toHaveAttribute('src', 'https://bucket.example/logo.png?sig=ab');
    expect(img.parentElement?.tagName).toBe('DIV');
  });

  it('falls back to the DFRNT wordmark when the image fails to load', async () => {
    server.use(meHandler(ME_US), logoHandler('https://bucket.example/expired.png'));
    renderWithProviders(<TenantLogo />);
    fireEvent.error(await logo());
    await waitFor(() => expect(screen.getByRole('img')).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB.png'));
  });

  it('falls back to the dark DFRNT wordmark on a dark surface, and to the Urgent art for the urgent tenant', async () => {
    server.use(meHandler(ME_US), logoHandler('https://bucket.example/expired.png'));
    const dark = renderWithProviders(<TenantLogo onShell />);
    fireEvent.error(await logo());
    await waitFor(() => expect(screen.getByRole('img')).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB_dark.png'));
    dark.unmount();

    server.use(meHandler(ME_NZ), logoHandler('https://bucket.example/expired.png'));
    renderWithProviders(<TenantLogo />);
    fireEvent.error(await logo());
    await waitFor(() => expect(screen.getByRole('img')).toHaveAttribute('src', '/images/urgentCouriersNewLogo.png'));
  });

  it('uses the default DFRNT logo when api/Logo fails or has no logo', async () => {
    server.use(meHandler(ME_US), http.get('/api/Logo', () => new HttpResponse(null, { status: 500 })));
    const failed = renderWithProviders(<TenantLogo />);
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB.png');
    failed.unmount();

    server.use(meHandler(ME_US), logoHandler(null));
    renderWithProviders(<TenantLogo />);
    expect(await logo()).toHaveAttribute('src', '/images/DFRNT_HorizLogo_RGB.png');
  });

  it('reserves its height while loading', () => {
    server.use(meHandler(ME_US), logoHandler());
    const { container } = renderWithProviders(<TenantLogo height={48} />);
    expect(container.querySelector('img')).toBeNull();
  });
});
