interface UpdateTenantResponse {
    success: boolean;
    redirectUrl?: string;
    message?: string;
}

function getRequestToken(): string {
    return document.querySelector<HTMLMetaElement>('meta[name="request-token"]')?.content ?? '';
}

// Tenant switcher — Bootstrap's dropdown JS handles open/close/anchor; this only
// wires the item clicks to the tenant-switch fetch. Items are <button.dropdown-item>.
function initTenantSwitcher(): void {
    const menu = document.getElementById('tenantMenu');
    const button = document.getElementById('tenantDropdown') as (HTMLButtonElement | null);
    if (!menu || !button) return;

    const label = button.querySelector<HTMLElement>('.tenant-name');

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
        const item = (event.target as HTMLElement).closest<HTMLElement>('.dropdown-item');
        if (!item) return;
        const tenantId = Number.parseInt(item.getAttribute('data-tenant-id') ?? '0', 10);
        void switchTenant(tenantId, item.textContent?.trim() ?? '');
    });
}

type ThemeChoice = 'light' | 'dark' | 'system';

// Light / Dark / System toggle. An inline script in <head> already applied the
// saved choice before first paint (to avoid a flash); this only wires the menu
// and keeps the icon + selected state in sync. Bootstrap handles open/close.
// 'dark'/'light' set data-theme on <html> (winning over the OS); 'system' clears
// it so the prefers-color-scheme rules in site.less take over.
function initThemeToggle(): void {
    const toggle = document.getElementById('themeToggle');
    const menu = document.getElementById('themeMenu');
    if (!toggle || !menu) return;

    // The button holds all three Lucide icons (sun / moon / sun-moon) inlined by
    // the <dfrnt-icon> TagHelper; CSS shows the one matching data-theme-icon.
    const icon = toggle.querySelector<HTMLElement>('.theme-toggle-icon');

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
        menu!.querySelectorAll<HTMLElement>('.dropdown-item').forEach(item => {
            item.classList.toggle('active', item.getAttribute('data-theme-choice') === choice);
        });
    }

    applyChoice(readChoice());

    menu.addEventListener('click', event => {
        const item = (event.target as HTMLElement).closest<HTMLElement>('.dropdown-item');
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
    initThemeToggle();
});

export {};
