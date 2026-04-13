declare const grecaptcha: {
    ready(callback: () => void): void;
    execute(siteKey: string, options: { action: string }): Promise<string>;
};

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('forgot-password-form') as HTMLFormElement;
    const messageArea = document.getElementById('messageArea') as HTMLElement;
    const emailInput = document.getElementById('Email') as HTMLInputElement;
    const submitButton = document.getElementById('submitButton') as HTMLButtonElement;
    const spinner = submitButton.querySelector('.spinner-border') as HTMLElement;
    const recaptchaSiteKey = document.querySelector<HTMLMetaElement>('meta[name="recaptcha-site-key"]')?.content;

    function showMessage(message: string, isError = false): void {
        messageArea.textContent = message;
        messageArea.classList.remove('d-none', 'alert-success', 'alert-danger');
        messageArea.classList.add(isError ? 'alert-danger' : 'alert-success');
    }

    function validateEmail(email: string): boolean {
        const re = /^(([^<>()\[\]\\.,;:\s@"]+(\.[[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
        return re.test(String(email).toLowerCase());
    }

    function setLoading(isLoading: boolean): void {
        submitButton.disabled = isLoading;
        spinner.classList.toggle('d-none', !isLoading);
        const btnText = submitButton.querySelector('.button-text');
        if (btnText) btnText.textContent = isLoading ? 'Processing...' : 'Reset Password';
    }

    emailInput.addEventListener('input', function (this: HTMLInputElement) {
        if (validateEmail(this.value)) {
            this.classList.remove('is-invalid');
            this.classList.add('is-valid');
        } else {
            this.classList.remove('is-valid');
            this.classList.add('is-invalid');
        }
    });

    form.addEventListener('submit', e => {
        e.preventDefault();

        if (!validateEmail(emailInput.value)) {
            emailInput.classList.add('is-invalid');
            return;
        }

        setLoading(true);

        grecaptcha.ready(() => {
            grecaptcha.execute(recaptchaSiteKey!, {action: 'forgot_password'}).then(token => {
                (document.getElementById('g-recaptcha-response') as HTMLInputElement).value = token;
                submitForm();
            });
        });
    });

    function submitForm(): void {
        fetch(form.action, {
            method: 'POST',
            body: new FormData(form),
        })
            .then(response => response.json())
            .then(data => {
                setLoading(false);
                if (data.success) {
                    showMessage(data.message);
                    form.reset();
                    emailInput.classList.remove('is-valid');
                } else {
                    showMessage(data.message, true);
                }
            })
            .catch(() => {
                setLoading(false);
                showMessage('An error occurred. Please try again later.', true);
            });
    }
});
