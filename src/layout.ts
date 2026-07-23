interface UpdateTenantResponse {
    success: boolean;
    redirectUrl?: string;
    message?: string;
}

// @material/web md-menu surface we toggle imperatively.
interface MdMenu extends HTMLElement {
    open: boolean;
}

function getRequestToken(): string {
    return document.querySelector<HTMLMetaElement>('meta[name="request-token"]')?.content ?? '';
}

function initTenantSwitcher(): void {
    const menu = document.getElementById('tenantMenu') as MdMenu | null;
    const button = document.getElementById('tenantDropdown') as (HTMLElement & { disabled: boolean }) | null;
    if (!menu || !button) return;

    const label = button.querySelector<HTMLElement>('.tenant-name');

    button.addEventListener('click', event => {
        event.stopPropagation();
        menu.open = !menu.open;
    });

    function setTenantLoading(isLoading: boolean): void {
        button!.disabled = isLoading;
    }

    function setTenantDropdownLabel(name: string): void {
        if (label) label.textContent = name;
    }

    // Surface a tenant-switch failure to the user. The backend returns an
    // actionable `message` (e.g. "no operator record in that tenant — ask an
    // administrator"); render it as a dismissible, auto-expiring alert pinned
    // top-right.
    function showTenantSwitchError(message: string): void {
        type AlertWithTimer = HTMLElement & { _hideTimer?: number };
        let alert = document.getElementById('tenantSwitchError') as AlertWithTimer | null;
        if (!alert) {
            alert = document.createElement('div') as AlertWithTimer;
            alert.id = 'tenantSwitchError';
            alert.setAttribute('role', 'alert');
            alert.className = 'alert alert-danger position-fixed top-0 end-0 m-3 shadow';
            alert.style.zIndex = '1080';
            alert.style.maxWidth = '420px';
            document.body.appendChild(alert);
        }
        alert.textContent = message;
        window.clearTimeout(alert._hideTimer);
        alert._hideTimer = window.setTimeout(() => alert?.remove(), 8000);
    }

    async function switchTenant(tenantId: number, tenantName: string): Promise<void> {
        setTenantLoading(true);
        try {
            const response = await fetch('/Account/UpdateCurrentTenant', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': getRequestToken(),
                },
                body: JSON.stringify({tenantId}),
            });
            const data = await response.json() as UpdateTenantResponse;

            if (!data.success) {
                const message = data.message && data.message.length > 0
                    ? data.message
                    : 'Could not switch tenant. Please try again.';
                console.error('Failed to update tenant:', message);
                showTenantSwitchError(message);
                setTenantLoading(false);
                return;
            }

            setTenantDropdownLabel(tenantName);

            // When the backend returns a redirectUrl, follow it (the destination
            // Hub validates a short-lived SSO token and issues a fresh cookie);
            // otherwise reload in place.
            if (typeof data.redirectUrl === 'string' && data.redirectUrl.length > 0) {
                window.location.href = data.redirectUrl;
            } else {
                window.location.reload();
            }
        } catch (error) {
            console.error('Error updating tenant:', error);
            setTenantLoading(false);
        }
    }

    menu.addEventListener('click', event => {
        const item = (event.target as HTMLElement).closest('md-menu-item');
        if (!item) return;
        const tenantId = Number.parseInt(item.getAttribute('data-tenant-id') ?? '0', 10);
        void switchTenant(tenantId, item.textContent?.trim() ?? '');
    });
}

function initProfileDropdown(): void {
    const trigger = document.getElementById('profileTrigger');
    const menu = document.getElementById('profileMenu') as MdMenu | null;
    if (!trigger || !menu) return;

    function toggle(event: Event): void {
        event.stopPropagation();
        menu!.open = !menu!.open;
    }

    trigger.addEventListener('click', toggle);
    trigger.addEventListener('keydown', event => {
        if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault();
            toggle(event);
        }
    });

    // md-menu closes itself on outside-click / Escape; mirror its open state on
    // the trigger so the chevron rotation (.active) stays in sync.
    menu.addEventListener('opened', () => trigger.classList.add('active'));
    menu.addEventListener('closed', () => trigger.classList.remove('active'));
}

type ThemeChoice = 'light' | 'dark' | 'system';

// Light / Dark / System toggle. An inline script in <head> already applied the
// saved choice before first paint (to avoid a flash); this only wires the menu
// and keeps the icon + selected state in sync. 'dark'/'light' set data-theme on
// <html> (winning over the OS); 'system' clears it so the prefers-color-scheme
// rules in site.less take over.
function initThemeToggle(): void {
    const toggle = document.getElementById('themeToggle');
    const menu = document.getElementById('themeMenu') as MdMenu | null;
    if (!toggle || !menu) return;

    // The button holds all three Lucide icons (sun / moon / sun-moon) inlined by
    // the <dfrnt-icon> TagHelper; CSS shows the one matching data-theme-icon.
    const icon = toggle.querySelector<HTMLElement>('md-icon');

    function readChoice(): ThemeChoice {
        try {
            const stored = localStorage.getItem('theme');
            if (stored === 'light' || stored === 'dark') return stored;
        } catch {
            // localStorage unavailable (private mode / disabled) — fall back to system.
        }
        return 'system';
    }

    function applyChoice(choice: ThemeChoice): void {
        if (choice === 'system') {
            document.documentElement.removeAttribute('data-theme');
        } else {
            document.documentElement.setAttribute('data-theme', choice);
        }
        if (icon) icon.dataset.themeIcon = choice;
        menu!.querySelectorAll('md-menu-item').forEach(item => {
            item.toggleAttribute('selected', item.getAttribute('data-theme-choice') === choice);
        });
    }

    applyChoice(readChoice());

    toggle.addEventListener('click', event => {
        event.stopPropagation();
        menu.open = !menu.open;
    });

    menu.addEventListener('click', event => {
        const item = (event.target as HTMLElement).closest('md-menu-item');
        const choice = item?.getAttribute('data-theme-choice') as ThemeChoice | null;
        if (!choice) return;
        try {
            localStorage.setItem('theme', choice);
        } catch {
            // Persistence best-effort; the choice still applies for this session.
        }
        applyChoice(choice);
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initTenantSwitcher();
    initProfileDropdown();
    initThemeToggle();
});

export {};
