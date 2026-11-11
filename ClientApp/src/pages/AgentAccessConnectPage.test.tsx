import { http, HttpResponse } from 'msw';
import { fireEvent, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { AgentAccessConnectPage as ConnectPage } from '../api/types';
import { logoHandler, meHandler } from '../test/handlers';
import { server } from '../test/server';
import { renderWithProviders } from '../test/utils';
import AgentAccessConnectPage from './AgentAccessConnectPage';

const page = (over: Partial<ConnectPage> = {}): ConnectPage => ({
  kind: 'consent',
  clientName: 'Claude',
  verifiedVendor: 'Anthropic',
  clientHost: null,
  resourceHost: 'mcp.urgent.co.nz',
  redirectHost: 'claude.ai',
  currentOrganisation: null,
  hubOrganisation: null,
  canSwitch: false,
  error: null,
  parameters: [
    { name: 'client_id', value: 'dfrnt-claude' },
    { name: 'state', value: 'xyz' },
  ],
  antiforgeryFieldName: '__RequestVerificationToken',
  antiforgeryToken: 'af-token',
  ...over,
});

function renderPage(body: ConnectPage | Response, ticket = 't1') {
  const tickets: string[] = [];
  server.use(
    meHandler(),
    logoHandler(),
    http.get('/api/agent-access/connect', ({ request }) => {
      tickets.push(new URL(request.url).searchParams.get('ticket') ?? '');
      return body instanceof Response ? body : HttpResponse.json(body);
    }),
  );
  const view = renderWithProviders(<AgentAccessConnectPage />, { route: `/agent-access/connect?ticket=${ticket}` });
  return { tickets, ...view };
}

const hidden = (form: HTMLFormElement) =>
  Object.fromEntries(
    Array.from(form.querySelectorAll<HTMLInputElement>('input[type="hidden"]')).map((i) => [i.name, i.value]),
  );

describe('AgentAccessConnectPage', () => {
  it('reads the page for the ticket in the URL', async () => {
    const { tickets } = renderPage(page(), 'abc');
    await screen.findByRole('heading', { name: 'Connect Claude' });
    expect(tickets).toEqual(['abc']);
  });

  it('names a configured app with its vendor and says what it can and cannot do', async () => {
    renderPage(page());
    expect(await screen.findByRole('heading', { name: 'Connect Claude' })).toBeInTheDocument();
    expect(screen.getByText(/Claude by Anthropic wants to use your DFRNT account on/)).toBeInTheDocument();
    expect(screen.getByText('mcp.urgent.co.nz')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Claude will be able to' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: "Claude won't be able to" })).toBeInTheDocument();
    expect(screen.getByText('Create, change or delete anything.')).toBeInTheDocument();
    expect(screen.getByText(/You'll go back to claude.ai/)).toBeInTheDocument();
  });

  it('names an app from a metadata document by the host it came from', async () => {
    renderPage(page({ clientName: 'Claude Connector', verifiedVendor: null, clientHost: 'claude.ai' }));
    expect(await screen.findByText(/Claude Connector from claude.ai wants to use your DFRNT account/)).toBeInTheDocument();
  });

  it('posts the original request, the antiforgery token and the decision back to the authorize endpoint', async () => {
    renderPage(page());
    const allow = await screen.findByRole('button', { name: 'Allow access' });
    const deny = screen.getByRole('button', { name: "Don't allow" });
    const form = allow.closest('form')!;

    expect(form).toHaveAttribute('method', 'post');
    expect(form).toHaveAttribute('action', '/connect/authorize');
    expect(hidden(form)).toEqual({ client_id: 'dfrnt-claude', state: 'xyz', __RequestVerificationToken: 'af-token' });
    expect(allow).toHaveAttribute('name', 'decision');
    expect(allow).toHaveAttribute('value', 'allow');
    expect(deny).toHaveAttribute('value', 'deny');
  });

  it('sends the decision only once, however many times it is clicked', async () => {
    renderPage(page());
    const form = (await screen.findByRole('button', { name: 'Allow access' })).closest('form')!;

    expect(fireEvent.submit(form)).toBe(true);
    expect(fireEvent.submit(form)).toBe(false);
    expect(screen.getByRole('progressbar', { hidden: true })).toHaveAccessibleName('Connecting');
  });

  it('lets the person choose again when they come back to the page with the back button', async () => {
    renderPage(page());
    const form = (await screen.findByRole('button', { name: 'Allow access' })).closest('form')!;
    fireEvent.submit(form);
    expect(screen.getByRole('progressbar', { hidden: true })).toHaveAttribute('aria-hidden', 'false');

    const restored = new Event('pageshow');
    Object.defineProperty(restored, 'persisted', { value: true });
    fireEvent(window, restored);

    expect(screen.getByRole('progressbar', { hidden: true })).toHaveAttribute('aria-hidden', 'true');
    expect(fireEvent.submit(form)).toBe(true);
  });

  it('offers a switch to the organisation the app is connecting to', async () => {
    renderPage(page({ kind: 'switch', currentOrganisation: 'DFRNT', hubOrganisation: 'Urgent Couriers', canSwitch: true }));
    expect(await screen.findByText(/You're logged in to/)).toHaveTextContent(
      "You're logged in to DFRNT, but Claude is connecting to Urgent Couriers.",
    );
    expect(screen.getByRole('button', { name: 'Switch to Urgent Couriers' })).toHaveAttribute('value', 'switch');
    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveAttribute('value', 'cancel');
  });

  it('only offers cancel to someone outside that organisation', async () => {
    renderPage(page({ kind: 'switch', currentOrganisation: 'DFRNT', hubOrganisation: 'Urgent Couriers', canSwitch: false }));
    expect(await screen.findByText(/doesn't have access to Urgent Couriers/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Switch to/ })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('shows why a switch failed', async () => {
    renderPage(
      page({
        kind: 'switch',
        currentOrganisation: 'DFRNT',
        hubOrganisation: 'Urgent Couriers',
        canSwitch: true,
        error: "Couldn't switch organisation. Try again.",
      }),
    );
    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't switch organisation. Try again.");
  });

  it('says an expired request has to start again from the app, with nothing to submit', async () => {
    renderPage(
      HttpResponse.json(
        { error: "Couldn't open this connection request. It has expired. Go back to the app and connect again." },
        { status: 400 },
      ),
    );
    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't open this connection request. It has expired.");
    expect(screen.queryByRole('button', { name: 'Allow access' })).not.toBeInTheDocument();
  });

  it('asks the app to start again when the URL has no ticket', async () => {
    renderWithProviders(<AgentAccessConnectPage />, { route: '/agent-access/connect' });
    expect(await screen.findByRole('alert')).toHaveTextContent(
      "Couldn't open this connection request. The link is incomplete. Go back to the app and connect again.",
    );
  });
});
