function getCurrentTenantNameFromUrl(): string | undefined {
    const url = new URL(window.location.href);
    const hostnameParts = url.hostname.split('.');
    return hostnameParts[1];
}

function replaceTenantNameAndRefresh(newTenantName: string): void {
    const url = new URL(window.location.href);
    let hostnameParts = url.hostname.split('.');

    if (newTenantName === "DFRNT") {
        hostnameParts.splice(1, 1);
    } else {
        hostnameParts[1] = newTenantName;
    }

    hostnameParts = hostnameParts.filter(part => part !== '');
    url.hostname = hostnameParts.join('.');

    sessionStorage.setItem('urlJustChanged', 'true');
    window.location.href = url.href;
}

function checkAndSyncTenantName(currentTenantName: string, hasTenants: boolean): void {
    if (!hasTenants) return;

    const urlTenantName = getCurrentTenantNameFromUrl();

    if (urlTenantName === 'local' || urlTenantName === 'staging') {
        return;
    }

    if (urlTenantName !== currentTenantName) {
        replaceTenantNameAndRefresh(currentTenantName);
    }
}

function initTenantSync(currentTenantName: string, hasTenants: boolean): void {
    if (sessionStorage.getItem('urlJustChanged') === 'true') {
        sessionStorage.removeItem('urlJustChanged');
        return;
    }
    checkAndSyncTenantName(currentTenantName, hasTenants);
}

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
        dropdownMenu.addEventListener('click', e => {
            const target = e.target as HTMLElement;
            if (target && target.nodeName === 'A') {
                e.preventDefault();
                dropdownMenu.classList.remove('show');
                if (tenantDropdown) {
                    tenantDropdown.classList.remove('show');
                    tenantDropdown.setAttribute('aria-expanded', 'false');
                }
                const selectedTenantId = parseInt(target.getAttribute('data-tenant-id') ?? '0', 10);
                const selectedTenantCode = target.getAttribute('data-tenant-code');
                const selectedTenantName = target.textContent;

                setTenantLoading(true);

                fetch('/Account/UpdateCurrentTenant', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': requestToken ?? ''
                    },
                    body: JSON.stringify({tenantId: selectedTenantId})
                })
                    .then(response => response.json())
                    .then(data => {
                        if (data.success) {
                            if (tenantDropdown) {
                                tenantDropdown.textContent = selectedTenantName + ' ';
                                const spinner = tenantDropdown.querySelector('.spinner-border');
                                if (spinner) {
                                    tenantDropdown.appendChild(spinner);
                                }
                            }

                            const urlTenantName = getCurrentTenantNameFromUrl();
                            if (urlTenantName === 'local' || urlTenantName === 'staging') {
                                window.location.reload();
                                return;
                            }
                            replaceTenantNameAndRefresh(selectedTenantCode ?? '');
                        } else {
                            console.error('Failed to update tenant');
                            setTenantLoading(false);
                        }
                    })
                    .catch(error => {
                        console.error('Error updating tenant:', error);
                        setTenantLoading(false);
                    });
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

    const body = document.body;
    const currentTenantCode = body.dataset.tenantCode ?? '';
    const hasTenants = body.dataset.hasTenants === 'true';

    initTenantSync(currentTenantCode, hasTenants);
});
