import type { ZxcvbnFactory } from '@zxcvbn-ts/core';

type Zxcvbn = InstanceType<typeof ZxcvbnFactory>;

// Composition rule mirrors the backend [StrongPassword] attribute — the server
// rejects anything that fails it, so the client MUST keep enforcing it too.
const PASSWORD_REGEX = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$/;
const PASSWORD_INVALID_MESSAGE = 'Password must be at least 8 characters long, contain at least one uppercase letter, one lowercase letter, one number, and one special character.';
const CONFIRM_MISMATCH_MESSAGE = "Passwords don't match";

// zxcvbn strength gate layered ON TOP of the composition rule: even a
// rule-compliant password (e.g. "Password1!") can be trivially guessable, so we
// also require a minimum crackability score. 0-4; 3 = "good", the value
// zxcvbn's own docs suggest as a floor for important accounts.
const MIN_STRENGTH_SCORE = 3;
const WEAK_PASSWORD_MESSAGE = 'This password is too easy to guess. Try a longer or less predictable one.';
const STRENGTH_LABELS = ['Very weak', 'Weak', 'Fair', 'Good', 'Strong'] as const;

// zxcvbn-ts + its English dictionary are ~800 KB gzipped, so they're pulled in
// on demand via dynamic import() (esbuild emits them as a separate chunk) and
// memoised. Preloaded on first focus of the password field, so the estimator is
// almost always ready by the time the user submits.
let factoryPromise: Promise<Zxcvbn> | null = null;
function loadZxcvbn(): Promise<Zxcvbn> {
    factoryPromise ??= (async () => {
        const [{ ZxcvbnFactory }, common, en] = await Promise.all([
            import('@zxcvbn-ts/core'),
            import('@zxcvbn-ts/language-common'),
            import('@zxcvbn-ts/language-en'),
        ]);
        return new ZxcvbnFactory({
            dictionary: { ...common.dictionary, ...en.dictionary },
            graphs: common.adjacencyGraphs,
            translations: en.translations,
        });
    })();
    return factoryPromise;
}

function isStrongPassword(pwd: string): boolean {
    return PASSWORD_REGEX.test(pwd);
}

// Set/clear a Bootstrap inline validation error on a field. The error text goes
// in the adjacent .invalid-feedback (a sibling of the input inside .form-floating).
function setFieldError(field: HTMLInputElement, message: string): void {
    field.classList.toggle('is-invalid', message.length > 0);
    const feedback = field.parentElement?.querySelector<HTMLElement>('.invalid-feedback');
    if (feedback) feedback.textContent = message;
}

document.addEventListener('DOMContentLoaded', () => {
    const form = document.querySelector('.needs-validation') as HTMLFormElement;
    const password = document.getElementById('Password') as HTMLInputElement;
    const confirmPassword = document.getElementById('ConfirmPassword') as HTMLInputElement;
    const button = document.getElementById('resetButton') as HTMLButtonElement;
    const label = button.querySelector<HTMLElement>('.button-label');
    const resetFailed = document.querySelector<HTMLElement>('.auth-wrapper')?.dataset.resetFailed === 'true';

    // Strength-meter elements (present on the reset view only). strengthBar is the
    // Bootstrap .progress-bar fill; its width tracks the score.
    const strength = document.getElementById('passwordStrength');
    const strengthBar = document.getElementById('strengthBar');
    const strengthLabel = document.getElementById('strengthLabel');
    const strengthFeedback = document.getElementById('strengthFeedback');

    // The score of the value we last ran through zxcvbn; -1 while unknown (not
    // yet loaded / value changed since). Kept so the synchronous composition
    // check can factor in strength without blocking on the async estimator.
    let lastScoredValue = '';
    let lastScore = -1;

    // Update the label span, not button.textContent, so the button icon survives.
    function setSubmitting(isSubmitting: boolean): void {
        button.disabled = isSubmitting;
        if (label) label.textContent = isSubmitting ? 'Processing...' : 'Reset Password';
    }

    function currentScore(): number {
        return lastScoredValue === password.value ? lastScore : -1;
    }

    // Sync composition + strength error text. Strength only fails the field once
    // we actually have a score for the current value (currentScore() >= 0).
    function refreshPasswordError(): void {
        if (!password.value) {
            setFieldError(password, '');
            return;
        }
        const score = currentScore();
        if (!isStrongPassword(password.value)) {
            setFieldError(password, PASSWORD_INVALID_MESSAGE);
        } else if (score >= 0 && score < MIN_STRENGTH_SCORE) {
            setFieldError(password, WEAK_PASSWORD_MESSAGE);
        } else {
            setFieldError(password, '');
        }
    }

    function paintMeter(score: number): void {
        if (strength) {
            strength.hidden = false;
            strength.dataset.score = String(score);
        }
        if (strengthBar) strengthBar.style.width = `${((score + 1) / STRENGTH_LABELS.length) * 100}%`;
        if (strengthLabel) strengthLabel.textContent = STRENGTH_LABELS[score];
    }

    // Async: awaits the lazily-loaded estimator, then paints the meter and
    // refreshes the field error. Bails if the value changed while loading.
    async function updateStrengthMeter(): Promise<void> {
        const value = password.value;
        if (!value) {
            if (strength) strength.hidden = true;
            return;
        }
        const factory = await loadZxcvbn();
        if (password.value !== value) return;

        const result = factory.check(value);
        lastScoredValue = value;
        lastScore = result.score;
        paintMeter(result.score);
        if (strengthFeedback) {
            strengthFeedback.textContent = result.feedback.warning || result.feedback.suggestions[0] || '';
        }
        refreshPasswordError();
    }

    function validateConfirmField(): void {
        const mismatch = Boolean(confirmPassword.value) && password.value !== confirmPassword.value;
        setFieldError(confirmPassword, mismatch ? CONFIRM_MISMATCH_MESSAGE : '');
    }

    async function handleSubmit(): Promise<void> {
        refreshPasswordError();
        validateConfirmField();

        const compositionOk = form.checkValidity()
            && isStrongPassword(password.value)
            && password.value === confirmPassword.value;
        if (!compositionOk) {
            form.reportValidity();
            return;
        }

        // Composition passed — make sure the strength gate is satisfied before
        // letting the POST through (loads the estimator now if focus didn't).
        const factory = await loadZxcvbn();
        const result = factory.check(password.value);
        lastScoredValue = password.value;
        lastScore = result.score;
        paintMeter(result.score);
        if (strengthFeedback) {
            strengthFeedback.textContent = result.feedback.warning || result.feedback.suggestions[0] || '';
        }

        if (result.score < MIN_STRENGTH_SCORE) {
            refreshPasswordError();
            form.reportValidity();
            return;
        }

        setSubmitting(true);
        form.submit(); // bypasses this handler (no submit event) — already validated
    }

    if (resetFailed) setSubmitting(false);

    // Warm the estimator chunk as soon as the user engages the password field.
    password.addEventListener('focusin', () => { void loadZxcvbn(); });
    password.addEventListener('input', () => {
        refreshPasswordError();
        void updateStrengthMeter();
    });
    confirmPassword.addEventListener('input', validateConfirmField);

    form.addEventListener('submit', event => {
        event.preventDefault();
        event.stopPropagation();
        void handleSubmit();
    });
});

export {};
