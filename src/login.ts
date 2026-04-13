document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('loginForm') as HTMLFormElement;
    const button = document.getElementById('loginButton') as HTMLButtonElement;
    const spinner = button.querySelector('.spinner-border') as HTMLElement;
    const buttonText = button.querySelector('.button-text') as HTMLElement;
    const emailInput = document.getElementById('Email') as HTMLInputElement;
    const emailFeedback = emailInput.nextElementSibling as HTMLElement;
    const loginFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.loginFailed === 'true';

    function resetButton(): void {
        button.disabled = false;
        spinner.classList.add('d-none');
        buttonText.textContent = 'Log in';
    }

    function validateEmail(email: string): boolean {
        const re = /^(([^<>()\[\]\\.,;:\s@"]+(\.[[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
        return re.test(String(email).toLowerCase());
    }

    function updateEmailValidationUI(): void {
        if (!emailInput.value) {
            emailInput.classList.remove('is-valid', 'is-invalid');
            emailFeedback.style.display = 'none';
            return;
        }
        const isValid = validateEmail(emailInput.value);
        emailInput.classList.toggle('is-valid', isValid);
        emailInput.classList.toggle('is-invalid', !isValid);
        emailFeedback.style.display = isValid ? 'none' : 'block';
    }

    if (loginFailed) {
        resetButton();
    }

    form.addEventListener('submit', e => {
        if (form.checkValidity() && validateEmail(emailInput.value)) {
            form.classList.add('submitting');
            if (document.activeElement instanceof HTMLElement) {
                document.activeElement.blur();
            }
            button.blur();
            button.disabled = true;
            spinner.classList.remove('d-none');
            buttonText.textContent = 'Logging in...';
        } else {
            e.preventDefault();
            e.stopPropagation();
            updateEmailValidationUI();
        }
        form.classList.add('was-validated');
    });

    emailInput.addEventListener('input', updateEmailValidationUI);
    emailInput.addEventListener('blur', updateEmailValidationUI);

    form.querySelectorAll<HTMLInputElement | HTMLSelectElement>('input, select').forEach(element => {
        function clearValidation(this: HTMLElement): void {
            if (this !== emailInput && form.classList.contains('was-validated')) {
                form.classList.remove('was-validated');
            }
        }
        element.addEventListener('input', clearValidation);
        element.addEventListener('change', clearValidation);
    });
});
