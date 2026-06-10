// Cross-subdomain URL-swap helpers were removed in favour of always reloading
// the current page after a tenant switch. Hub uses per-tenant Cookie.Domain
// scoping (each subdomain has its own session cookie), so redirecting to a
// different tenant's subdomain leaves the destination reading a stale cookie
// from a previous session. Reloading in place lets the just-issued cookie's
// claims take effect within the current subdomain. A future cross-domain SSO
// flow can restore "URL matches active tenant" properly when multi-tenant
// users land in production.

document.addEventListener('DOMContentLoaded', () => {
    const dropdownMenu = document.querySelector('.dropdown-menu');
    const tenantDropdown = document.getElementById('tenantDropdown') as HTMLButtonElement | null;
    const requestToken = document.querySelector<HTMLMetaElement>('meta[name="request-token"]')?.content;

    function setTenantLoading(isLoading: boolean): void {
        if (!tenantDropdown) return;
        tenantDropdown.disabled = isLoading;
        const tenantSpinner = tenantDropdown.querySelector('.spinner-border');
        if (tenantSpinner) {
            tenantSpinner.classList.toggle('d-none', !isLoading);
        }
    }

    // Surface a tenant-switch failure to the user. The backend returns an
    // actionable `message` (e.g. "no operator record in that tenant — ask an
    // administrator"); previously it was discarded to console only, so the
    // switcher just stopped spinning with no explanation. Render it as a
    // dismissible, auto-expiring Bootstrap alert pinned top-right.
    function showTenantSwitchError(message: string): void {
        let alert = document.getElementById('tenantSwitchError');
        if (!alert) {
            alert = document.createElement('div');
            alert.id = 'tenantSwitchError';
            alert.setAttribute('role', 'alert');
            alert.className = 'alert alert-danger position-fixed top-0 end-0 m-3 shadow';
            alert.style.zIndex = '1080';
            alert.style.maxWidth = '420px';
            document.body.appendChild(alert);
        }
        alert.textContent = message;
        window.clearTimeout((alert as HTMLElement & { _hideTimer?: number })._hideTimer);
        (alert as HTMLElement & { _hideTimer?: number })._hideTimer = window.setTimeout(() => {
            alert?.remove();
        }, 8000);
    }

    if (dropdownMenu) {
        dropdownMenu.addEventListener('click', async e => {
            const target = e.target as HTMLElement;
            if (target && target.nodeName === 'A') {
                e.preventDefault();
                dropdownMenu.classList.remove('show');
                if (tenantDropdown) {
                    tenantDropdown.classList.remove('show');
                    tenantDropdown.setAttribute('aria-expanded', 'false');
                }
                const selectedTenantId = parseInt(target.getAttribute('data-tenant-id') ?? '0', 10);
                const selectedTenantName = target.textContent;

                setTenantLoading(true);

                try {
                    const response = await fetch('/Account/UpdateCurrentTenant', {
                        method: 'POST',
                        headers: {
                            'Content-Type': 'application/json',
                            'RequestVerificationToken': requestToken ?? ''
                        },
                        body: JSON.stringify({tenantId: selectedTenantId})
                    });
                    const data = await response.json();

                    if (data.success) {
                        if (tenantDropdown) {
                            tenantDropdown.textContent = selectedTenantName + ' ';
                            const spinner = tenantDropdown.querySelector('.spinner-border');
                            if (spinner) {
                                tenantDropdown.appendChild(spinner);
                            }
                        }

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
                    } else {
                        const message = typeof data.message === 'string' && data.message.length > 0
                            ? data.message
                            : 'Could not switch tenant. Please try again.';
                        console.error('Failed to update tenant:', message);
                        showTenantSwitchError(message);
                        setTenantLoading(false);
                    }
                } catch (error) {
                    console.error('Error updating tenant:', error);
                    setTenantLoading(false);
                }
            }
        });
    }

    const profileDropdown = document.querySelector('.profile-dropdown');
    const profileIconUsername = document.querySelector('.profile-icon-username');
    const profileMenu = document.querySelector('.profile-menu') as HTMLElement | null;

    if (profileDropdown && profileIconUsername && profileMenu) {
        profileIconUsername.addEventListener('click', e => {
            e.stopPropagation();
            profileDropdown.classList.toggle('active');
            profileMenu.style.display = profileMenu.style.display === 'block' ? 'none' : 'block';
        });

        document.addEventListener('click', e => {
            if (!profileDropdown.contains(e.target as Node)) {
                profileDropdown.classList.remove('active');
                profileMenu.style.display = 'none';
            }
        });
    }

});
