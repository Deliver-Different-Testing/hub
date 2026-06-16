interface GenerateApiKeyResponse {
    success: boolean;
    apiKey?: string;
    message?: string;
}

const COPIED_RESET_DELAY_MS = 2000;

function getRequestVerificationToken(): string {
    return document.querySelector<HTMLInputElement>('input[name="__RequestVerificationToken"]')!.value;
}

function showApiKeyError(message: string): void {
    const errorEl = document.getElementById('apiKeyError');
    const inputEl = document.getElementById('ApiKey');
    if (errorEl) errorEl.textContent = message;
    if (inputEl) inputEl.classList.add('is-invalid');
}

function setButtonIcon(btn: HTMLButtonElement, iconName: string, label: string): void {
    btn.replaceChildren();
    const span = document.createElement('span');
    span.className = 'material-symbols-outlined';
    span.style.fontSize = '18px';
    span.style.verticalAlign = 'middle';
    span.textContent = iconName;
    btn.append(span, ` ${label}`);
}

function createCopyButton(): HTMLButtonElement {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'btn btn-outline-secondary';
    btn.id = 'copyApiKey';
    btn.title = 'Copy to clipboard';
    setButtonIcon(btn, 'content_copy', 'Copy');
    btn.addEventListener('click', copyApiKeyToClipboard);
    return btn;
}

async function copyApiKeyToClipboard(): Promise<void> {
    const apiKeyInput = document.getElementById('ApiKey') as HTMLInputElement;
    const btn = document.getElementById('copyApiKey') as HTMLButtonElement;

    try {
        await navigator.clipboard.writeText(apiKeyInput.value);
        setButtonIcon(btn, 'check', 'Copied!');
        btn.classList.replace('btn-outline-secondary', 'btn-success');

        setTimeout(() => {
            setButtonIcon(btn, 'content_copy', 'Copy');
            btn.classList.replace('btn-success', 'btn-outline-secondary');
        }, COPIED_RESET_DELAY_MS);
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

        const apiKeyInput = document.getElementById('ApiKey') as HTMLInputElement;
        apiKeyInput.value = result.apiKey ?? '';

        const generateButton = document.getElementById('generateApiKey');
        generateButton?.parentNode?.replaceChild(createCopyButton(), generateButton);
    } catch (error) {
        showApiKeyError('An error occurred while generating the API key');
        console.error('Error:', error);
    }
}

document.addEventListener('DOMContentLoaded', () => {
    document.getElementById('generateApiKey')?.addEventListener('click', generateApiKey);
    document.getElementById('copyApiKey')?.addEventListener('click', copyApiKeyToClipboard);
});
