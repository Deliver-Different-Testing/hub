interface GenerateApiKeyResponse {
    success: boolean;
    apiKey?: string;
    message?: string;
}

const COPIED_RESET_DELAY_MS = 2000;

function getRequestVerificationToken(): string {
    return document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')!.value;
}

function apiKeyField(): HTMLInputElement {
    return document.getElementById('ApiKey') as HTMLInputElement;
}

function showApiKeyError(message: string): void {
    apiKeyField().classList.add('is-invalid');
    const error = document.getElementById('apiKeyError');
    if (error) {
        error.textContent = message;
        error.classList.remove('d-none');
    }
}

function clearApiKeyError(): void {
    apiKeyField().classList.remove('is-invalid');
    document.getElementById('apiKeyError')?.classList.add('d-none');
}

// The copy button shows a copy icon + "Copy"; on success swap to a check icon +
// "Copied!", then revert. Both icons are pre-rendered by the <dfrnt-icon>
// TagHelper; we just toggle their visibility (no client-side SVG creation).
function setCopyButtonCopied(copied: boolean): void {
    const btn = document.getElementById('copyApiKey');
    if (!btn) return;
    btn.querySelector('.copy-icon-copy')?.classList.toggle('d-none', copied);
    btn.querySelector('.copy-icon-check')?.classList.toggle('d-none', !copied);
    const label = btn.querySelector<HTMLElement>('.copy-label');
    if (label) label.textContent = copied ? 'Copied!' : 'Copy';
}

async function copyApiKeyToClipboard(): Promise<void> {
    const btn = document.getElementById('copyApiKey');
    if (!btn) return;

    try {
        await navigator.clipboard.writeText(apiKeyField().value);
        setCopyButtonCopied(true);
        setTimeout(() => setCopyButtonCopied(false), COPIED_RESET_DELAY_MS);
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
        clearApiKeyError();

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
