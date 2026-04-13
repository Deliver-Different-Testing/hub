document.addEventListener('DOMContentLoaded', () => {
    const generateButton = document.getElementById('generateApiKey');
    if (generateButton) {
        generateButton.addEventListener('click', async () => {
            try {
                const response = await fetch('/Account/GenerateAPIKey', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': (document.querySelector('input[name="__RequestVerificationToken"]') as HTMLInputElement).value
                    }
                });

                if (!response.ok) {
                    throw new Error('Failed to generate API key');
                }

                const result = await response.json();
                if (result.success) {
                    (document.getElementById('ApiKey') as HTMLInputElement).value = result.apiKey;
                    const copyButton = document.createElement('button');
                    copyButton.type = 'button';
                    copyButton.className = 'btn btn-outline-secondary';
                    copyButton.id = 'copyApiKey';
                    copyButton.title = 'Copy to clipboard';
                    copyButton.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px;vertical-align:middle">content_copy</span> Copy';
                    copyButton.addEventListener('click', copyApiKeyToClipboard);
                    generateButton.parentNode!.replaceChild(copyButton, generateButton);
                } else {
                    document.getElementById('apiKeyError')!.textContent = result.message || 'Failed to generate API key';
                    document.getElementById('ApiKey')!.classList.add('is-invalid');
                }
            } catch (error) {
                document.getElementById('apiKeyError')!.textContent = 'An error occurred while generating the API key';
                document.getElementById('ApiKey')!.classList.add('is-invalid');
                console.error('Error:', error);
            }
        });
    }

    const copyButton = document.getElementById('copyApiKey');
    if (copyButton) {
        copyButton.addEventListener('click', copyApiKeyToClipboard);
    }
});

async function copyApiKeyToClipboard(): Promise<void> {
    const apiKeyInput = document.getElementById('ApiKey') as HTMLInputElement;
    const btn = document.getElementById('copyApiKey') as HTMLButtonElement;

    try {
        await navigator.clipboard.writeText(apiKeyInput.value);
        const originalContent = btn.innerHTML;
        btn.innerHTML = '<span class="material-symbols-outlined" style="font-size:18px;vertical-align:middle">check</span> Copied!';
        btn.classList.replace('btn-outline-secondary', 'btn-success');

        setTimeout(() => {
            btn.innerHTML = originalContent;
            btn.classList.replace('btn-success', 'btn-outline-secondary');
        }, 2000);
    } catch (err) {
        console.error('Failed to copy text:', err);
    }
}
