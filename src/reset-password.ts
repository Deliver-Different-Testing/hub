document.addEventListener('DOMContentLoaded', () => {
    const form = document.querySelector('.needs-validation') as HTMLFormElement;
    const password = document.getElementById("Password") as HTMLInputElement;
    const confirmPassword = document.getElementById("ConfirmPassword") as HTMLInputElement;
    const passwordFeedback = document.getElementById("passwordFeedback") as HTMLElement;
    const confirmPasswordFeedback = document.getElementById("confirmPasswordFeedback") as HTMLElement;
    const button = document.getElementById('resetButton') as HTMLButtonElement;
    const spinner = button.querySelector('.spinner-border') as HTMLElement;
    const buttonText = button.querySelector('.button-text') as HTMLElement;
    const resetFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.resetFailed === 'true';

    function resetButton(): void {
        button.disabled = false;
        spinner.classList.add('d-none');
        buttonText.textContent = 'Reset Password';
    }

    if (resetFailed) {
        resetButton();
    }

    const isPasswordValid = (pwd: string): boolean => /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$/.test(pwd);

    function validatePasswords(): void {
        if (password.value) {
            if (!isPasswordValid(password.value)) {
                password.setCustomValidity("Invalid password");
                passwordFeedback.textContent = "Password must be at least 8 characters long, contain at least one uppercase letter, one lowercase letter, one number, and one special character.";
            } else {
                password.setCustomValidity('');
            }
            form.classList.add('was-validated');
        } else {
            password.setCustomValidity('');
            form.classList.remove('was-validated');
        }

        if (confirmPassword.value) {
            if (password.value !== confirmPassword.value) {
                confirmPassword.setCustomValidity("Passwords don't match");
                confirmPasswordFeedback.textContent = "Passwords don't match";
            } else {
                confirmPassword.setCustomValidity('');
                confirmPasswordFeedback.textContent = "Please confirm your password.";
            }
            form.classList.add('was-validated');
        } else {
            confirmPassword.setCustomValidity('');
            confirmPasswordFeedback.textContent = "Please confirm your password.";
            if (!password.value) {
                form.classList.remove('was-validated');
            }
        }
    }

    password.addEventListener('input', validatePasswords);
    confirmPassword.addEventListener('input', validatePasswords);

    form.addEventListener('submit', event => {
        if (!form.checkValidity() || !isPasswordValid(password.value) || password.value !== confirmPassword.value) {
            event.preventDefault();
            event.stopPropagation();
        } else {
            button.disabled = true;
            spinner.classList.remove('d-none');
            buttonText.textContent = 'Processing...';
        }
        form.classList.add('was-validated');
    }, false);
});
