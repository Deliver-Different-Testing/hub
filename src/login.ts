const EMAIL_REGEX = /^(([^<>()\[\]\\.,;:\s@"]+(\.([^<>()\[\]\\.,;:\s@"]+))*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;

function isValidEmail(email: string): boolean {
    return EMAIL_REGEX.test(email.toLowerCase());
}

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('loginForm') as HTMLFormElement;
    const button = document.getElementById('loginButton') as HTMLButtonElement;
    const spinner = button.querySelector('.spinner-border') as HTMLElement;
    const buttonText = button.querySelector('.button-text') as HTMLElement;
    const emailInput = document.getElementById('Email') as HTMLInputElement;
    const emailFeedback = emailInput.nextElementSibling as HTMLElement;
    const loginFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.loginFailed === 'true';

    function setSubmitting(isSubmitting: boolean): void {
        form.classList.toggle('submitting', isSubmitting);
        button.disabled = isSubmitting;
        spinner.classList.toggle('d-none', !isSubmitting);
        buttonText.textContent = isSubmitting ? 'Logging in...' : 'Log in';
    }

    function updateEmailValidationUI(): void {
        if (!emailInput.value) {
            emailInput.classList.remove('is-valid', 'is-invalid');
            emailFeedback.style.display = 'none';
            return;
        }
        const valid = isValidEmail(emailInput.value);
        emailInput.classList.toggle('is-valid', valid);
        emailInput.classList.toggle('is-invalid', !valid);
        emailFeedback.style.display = valid ? 'none' : 'block';
    }

    if (loginFailed) setSubmitting(false);

    form.addEventListener('submit', e => {
        const valid = form.checkValidity() && isValidEmail(emailInput.value);
        form.classList.add('was-validated');

        if (!valid) {
            e.preventDefault();
            e.stopPropagation();
            updateEmailValidationUI();
            return;
        }

        if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur();
        }
        button.blur();
        setSubmitting(true);
    });

    emailInput.addEventListener('input', updateEmailValidationUI);
    emailInput.addEventListener('blur', updateEmailValidationUI);

    form.querySelectorAll<HTMLInputElement | HTMLSelectElement>('input, select').forEach(element => {
        const clearValidation = (): void => {
            if (element !== emailInput && form.classList.contains('was-validated')) {
                form.classList.remove('was-validated');
            }
        };
        element.addEventListener('input', clearValidation);
        element.addEventListener('change', clearValidation);
    });
});
