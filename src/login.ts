const EMAIL_REGEX = /^(([^<>()\[\]\\.,;:\s@"]+(\.([^<>()\[\]\\.,;:\s@"]+))*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;

function isValidEmail(email: string): boolean {
    return EMAIL_REGEX.test(email.toLowerCase());
}

// @material/web surfaces driven imperatively. Text fields are form-associated,
// so `required` participates in form.checkValidity()/reportValidity() and the
// value is submitted under the field's `name`.
interface MdTextField extends HTMLElement {
    value: string;
    error: boolean;
    errorText: string;
}

interface MdButton extends HTMLElement {
    disabled: boolean;
}

const EMAIL_ERROR = 'Please enter a valid email address.';

document.addEventListener('DOMContentLoaded', () => {
    const form = document.getElementById('loginForm') as HTMLFormElement;
    const button = document.getElementById('loginButton') as MdButton;
    const label = button.querySelector<HTMLElement>('.button-label');
    const progress = document.getElementById('loginProgress');
    const emailField = document.getElementById('Email') as MdTextField;
    const loginFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.loginFailed === 'true';

    // Updates the label span (not button.textContent) so the slotted md-icon
    // survives; drives the indeterminate linear progress bar at the card top.
    function setSubmitting(isSubmitting: boolean): void {
        form.classList.toggle('submitting', isSubmitting);
        button.disabled = isSubmitting;
        if (label) label.textContent = isSubmitting ? 'Logging in...' : 'Log in';
        progress?.classList.toggle('active', isSubmitting);
        progress?.setAttribute('aria-hidden', String(!isSubmitting));
    }

    // Returns whether the email is valid; only surfaces an error once the user
    // has typed something (empty is left to the required-field check on submit).
    function validateEmailUI(): boolean {
        if (!emailField.value) {
            emailField.error = false;
            emailField.errorText = '';
            return false;
        }
        const valid = isValidEmail(emailField.value);
        emailField.error = !valid;
        emailField.errorText = valid ? '' : EMAIL_ERROR;
        return valid;
    }

    if (loginFailed) setSubmitting(false);

    // The mascot plays once on load, and only when the visitor hasn't asked for
    // reduced motion. Without `autoplay`/`loop` in the markup the player just
    // holds its first frame, so reduced-motion users get a still illustration.
    // Two variants are rendered (light/dark, theme-toggled by CSS); play the one
    // that's actually visible so we don't animate a display:none player.
    type LottiePlayer = HTMLElement & { play?: () => void };
    const players = Array.from(document.querySelectorAll<LottiePlayer>('.auth-lottie'));
    const lottie = players.find(p => p.offsetParent !== null) ?? players[0] ?? null;
    if (lottie && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
        const playOnce = (): void => lottie.play?.();
        if (typeof lottie.play === 'function') playOnce();
        else lottie.addEventListener('ready', playOnce, { once: true });
    }

    form.addEventListener('submit', e => {
        // Native constraint validation covers the required password / select;
        // the regex adds stricter email checking on top.
        const nativeValid = form.checkValidity();
        const emailValid = isValidEmail(emailField.value);
        validateEmailUI();

        if (!nativeValid || !emailValid) {
            e.preventDefault();
            e.stopPropagation();
            form.reportValidity();
            return;
        }

        if (document.activeElement instanceof HTMLElement) {
            document.activeElement.blur();
        }
        button.blur();
        setSubmitting(true);
    });

    emailField.addEventListener('input', validateEmailUI);
    emailField.addEventListener('blur', validateEmailUI);
});

export {};
