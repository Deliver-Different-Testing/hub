interface UpdateTenantResponse {
    success: boolean;
    redirectUrl?: string;
    message?: string;
}

function getRequestToken(): string {
    return document.querySelector<HTMLMetaElement>('meta[name="request-token"]')?.content ?? '';
}

function initTenantSwitcher(): void {
    const dropdownMenu = document.querySelector('.dropdown-menu');
    const tenantDropdown = document.getElementById('tenantDropdown') as HTMLButtonElement | null;
    if (!dropdownMenu) return;

    function setTenantLoading(isLoading: boolean): void {
        if (!tenantDropdown) return;
        tenantDropdown.disabled = isLoading;
        tenantDropdown.querySelector('.spinner-border')?.classList.toggle('d-none', !isLoading);
    }

    function setTenantDropdownLabel(label: string): void {
        if (!tenantDropdown) return;
        const spinner = tenantDropdown.querySelector('.spinner-border');
        tenantDropdown.textContent = `${label} `;
        if (spinner) tenantDropdown.appendChild(spinner);
    }

    function closeMenus(): void {
        dropdownMenu!.classList.remove('show');
        if (tenantDropdown) {
            tenantDropdown.classList.remove('show');
            tenantDropdown.setAttribute('aria-expanded', 'false');
        }
    }

    // Surface a tenant-switch failure to the user. The backend returns an
    // actionable `message` (e.g. "no operator record in that tenant — ask an
    // administrator"); previously it was discarded to console only, so the
    // switcher just stopped spinning with no explanation. Render it as a
    // dismissible, auto-expiring Bootstrap alert pinned top-right.
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

            // Phase 2: when the backend returns a redirectUrl, follow it. The
            // destination Hub validates a short-lived SSO token and issues a
            // fresh cookie scoped to its own subdomain — restoring "URL matches
            // active tenant". Fall back to in-place reload if no redirectUrl
            // (older backend / failure to compute the destination).
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

    dropdownMenu.addEventListener('click', event => {
        const target = event.target as HTMLElement | null;
        if (target?.nodeName !== 'A') return;
        event.preventDefault();
        closeMenus();
        const tenantId = Number.parseInt(target.dataset.tenantId ?? '0', 10);
        void switchTenant(tenantId, target.textContent ?? '');
    });
}

function initProfileDropdown(): void {
    const profileDropdown = document.querySelector('.profile-dropdown');
    const profileIconUsername = document.querySelector('.profile-icon-username');
    const profileMenu = document.querySelector<HTMLElement>('.profile-menu');
    if (!profileDropdown || !profileIconUsername || !profileMenu) return;

    function closeProfile(): void {
        profileDropdown!.classList.remove('active');
        profileMenu!.style.display = 'none';
    }

    profileIconUsername.addEventListener('click', event => {
        event.stopPropagation();
        profileDropdown.classList.toggle('active');
        profileMenu.style.display = profileMenu.style.display === 'block' ? 'none' : 'block';
    });

    document.addEventListener('click', event => {
        if (!profileDropdown.contains(event.target as Node)) closeProfile();
    });
}

document.addEventListener('DOMContentLoaded', () => {
    initTenantSwitcher();
    initProfileDropdown();
});
