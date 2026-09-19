declare const grecaptcha: {
    ready(callback: () => void): void;
    execute(siteKey: string, options: { action: string }): Promise<string>;
};

const EMAIL_ERROR = 'Please enter a valid email address.';

// Set/clear a Bootstrap inline validation error on a field. The error text goes
// in the adjacent .invalid-feedback (a sibling of the input inside .form-floating).
function setFieldError(field: HTMLInputElement, message: string): void {
    field.classList.toggle('is-invalid', message.length > 0);
    const feedback = field.parentElement?.querySelector<HTMLElement>('.invalid-feedback');
    if (feedback) feedback.textContent = message;
}

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('forgot-password-form') as HTMLFormElement;
    const messageArea = document.getElementById('messageArea') as HTMLElement;
    const emailField = document.getElementById('Email') as HTMLInputElement;
    const submitButton = document.getElementById('submitButton') as HTMLButtonElement;
    const recaptchaSiteKey = document.querySelector<HTMLMetaElement>('meta[name="recaptcha-site-key"]')?.content;

    function showMessage(message: string, isError = false): void {
        messageArea.textContent = message;
        messageArea.classList.remove('d-none', 'alert-success', 'alert-danger');
        messageArea.classList.add(isError ? 'alert-danger' : 'alert-success');
    }

    function validateEmail(email: string): boolean {
        const re = /^(([^<>()\[\]\\.,;:\s@"]+(\.([^<>()\[\]\\.,;:\s@"]+))*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
        return re.test(String(email).toLowerCase());
    }

    function setEmailError(hasError: boolean): void {
        setFieldError(emailField, hasError ? EMAIL_ERROR : '');
    }

    // Update the label span (not button.textContent) so the icon survives —
    // the same pattern the login button uses.
    function setLoading(isLoading: boolean): void {
        submitButton.disabled = isLoading;
        const label = submitButton.querySelector<HTMLElement>('.button-label');
        if (label) label.textContent = isLoading ? 'Processing...' : 'Reset Password';
    }

    emailField.addEventListener('input', () => {
        setEmailError(emailField.value.length > 0 && !validateEmail(emailField.value));
    });

    form.addEventListener('submit', async e => {
        e.preventDefault();

        if (!validateEmail(emailField.value)) {
            setEmailError(true);
            return;
        }

        setLoading(true);

        await new Promise<void>(resolve => grecaptcha.ready(resolve));
        (document.getElementById('g-recaptcha-response') as HTMLInputElement).value =
            await grecaptcha.execute(recaptchaSiteKey!, {action: 'forgot_password'});
        await submitForm();
    });

    async function submitForm(): Promise<void> {
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
            });
            const data = await response.json();

            setLoading(false);
            if (data.success) {
                showMessage(data.message);
                form.reset();
                emailField.value = '';
                setEmailError(false);
            } else {
                showMessage(data.message, true);
            }
        } catch {
            setLoading(false);
            showMessage('An error occurred. Please try again later.', true);
        }
    }
});

export {};
