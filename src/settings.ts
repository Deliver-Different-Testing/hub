interface GenerateApiKeyResponse {
    success: boolean;
    apiKey?: string;
    message?: string;
}

interface MdTextField extends HTMLElement {
    value: string;
    error: boolean;
    errorText: string;
}

const COPIED_RESET_DELAY_MS = 2000;

function getRequestVerificationToken(): string {
    return document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')!.value;
}

function apiKeyField(): MdTextField {
    return document.getElementById('ApiKey') as MdTextField;
}

function showApiKeyError(message: string): void {
    const field = apiKeyField();
    field.error = true;
    field.errorText = message;
}

// The copy button is an <md-outlined-button> with an icon slot; swap its
// contents to signal the copied state, then revert. Built with safe DOM APIs
// (no innerHTML) — values are constants but this keeps it XSS-proof by design.
function setCopyButtonContent(btn: HTMLElement, icon: string, label: string): void {
    const iconEl = document.createElement('md-icon');
    iconEl.setAttribute('slot', 'icon');
    iconEl.textContent = icon;
    btn.replaceChildren(iconEl, ` ${label}`);
}

async function copyApiKeyToClipboard(): Promise<void> {
    const btn = document.getElementById('copyApiKey');
    if (!btn) return;

    try {
        await navigator.clipboard.writeText(apiKeyField().value);
        setCopyButtonContent(btn, 'check', 'Copied!');
        setTimeout(() => setCopyButtonContent(btn, 'content_copy', 'Copy'), COPIED_RESET_DELAY_MS);
    } catch (err) {
        console.error('Failed to copy text:', err);
    }
}

async function generateApiKey(): Promise<void> {
    try {
        const response = await fetch('/Account/GenerateAPIKey', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getRequestVerificationToken(),
            },
        });

        if (!response.ok) {
            showApiKeyError('Failed to generate API key');
            return;
        }

        const result = await response.json() as GenerateApiKeyResponse;
        if (!result.success) {
            showApiKeyError(result.message ?? 'Failed to generate API key');
            return;
        }

        const field = apiKeyField();
        field.value = result.apiKey ?? '';
        field.error = false;
        field.errorText = '';

        document.getElementById('generateApiKey')?.classList.add('d-none');
        document.getElementById('copyApiKey')?.classList.remove('d-none');
    } catch (error) {
        showApiKeyError('An error occurred while generating the API key');
        console.error('Error:', error);
    }
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('generateApiKey')?.addEventListener('click', generateApiKey);
    document.getElementById('copyApiKey')?.addEventListener('click', copyApiKeyToClipboard);
});

export {};
