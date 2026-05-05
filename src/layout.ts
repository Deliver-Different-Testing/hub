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

                        // Always reload in place. The new cookie issued for this subdomain
                        // carries the chosen tenant's claims; a reload re-renders the page
                        // (and any apps launched from it) against the updated session.
                        window.location.reload();
                    } else {
                        console.error('Failed to update tenant');
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
