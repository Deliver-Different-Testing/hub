const PASSWORD_REGEX = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$/;
const PASSWORD_INVALID_MESSAGE = 'Password must be at least 8 characters long, contain at least one uppercase letter, one lowercase letter, one number, and one special character.';
const CONFIRM_DEFAULT_MESSAGE = 'Please confirm your password.';
const CONFIRM_MISMATCH_MESSAGE = "Passwords don't match";

function isStrongPassword(pwd: string): boolean {
    return PASSWORD_REGEX.test(pwd);
}

document.addEventListener('DOMContentLoaded', () => {
    const form = document.querySelector('.needs-validation') as HTMLFormElement;
    const password = document.getElementById('Password') as HTMLInputElement;
    const confirmPassword = document.getElementById('ConfirmPassword') as HTMLInputElement;
    const passwordFeedback = document.getElementById('passwordFeedback') as HTMLElement;
    const confirmPasswordFeedback = document.getElementById('confirmPasswordFeedback') as HTMLElement;
    const button = document.getElementById('resetButton') as HTMLButtonElement;
    const spinner = button.querySelector('.spinner-border') as HTMLElement;
    const buttonText = button.querySelector('.button-text') as HTMLElement;
    const resetFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.resetFailed === 'true';

    function setSubmitting(isSubmitting: boolean): void {
        button.disabled = isSubmitting;
        spinner.classList.toggle('d-none', !isSubmitting);
        buttonText.textContent = isSubmitting ? 'Processing...' : 'Reset Password';
    }

    function validatePasswordField(): void {
        if (password.value && !isStrongPassword(password.value)) {
            password.setCustomValidity('Invalid password');
            passwordFeedback.textContent = PASSWORD_INVALID_MESSAGE;
        } else {
            password.setCustomValidity('');
        }
    }

    function validateConfirmField(): void {
        if (confirmPassword.value && password.value !== confirmPassword.value) {
            confirmPassword.setCustomValidity("Passwords don't match");
            confirmPasswordFeedback.textContent = CONFIRM_MISMATCH_MESSAGE;
        } else {
            confirmPassword.setCustomValidity('');
            confirmPasswordFeedback.textContent = CONFIRM_DEFAULT_MESSAGE;
        }
    }

    function validatePasswords(): void {
        validatePasswordField();
        validateConfirmField();
        const anyValue = Boolean(password.value || confirmPassword.value);
        form.classList.toggle('was-validated', anyValue);
    }

    if (resetFailed) setSubmitting(false);

    password.addEventListener('input', validatePasswords);
    confirmPassword.addEventListener('input', validatePasswords);

    form.addEventListener('submit', event => {
        const valid = form.checkValidity()
            && isStrongPassword(password.value)
            && password.value === confirmPassword.value;
        form.classList.add('was-validated');

        if (!valid) {
            event.preventDefault();
            event.stopPropagation();
            return;
        }

        setSubmitting(true);
    });
});
