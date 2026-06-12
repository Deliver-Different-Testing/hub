declare global {
    interface Window {
        ContactID: string;
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const hub = document.getElementById('urgent-hub');
    window.ContactID = hub?.dataset.contactId ?? '';
});

export {};
