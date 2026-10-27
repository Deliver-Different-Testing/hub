import { useState, type FormEvent } from 'react';
import { Alert, Anchor, Center, Checkbox, NativeSelect, PasswordInput, Stack, Text, TextInput, Title } from '@mantine/core';
import { useMutation } from '@tanstack/react-query';
import { LogIn } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { login } from '../api/hub';
import AuthCard from '../components/AuthCard';
import LoginLottie from '../components/LoginLottie';
import SubmitButton from '../components/SubmitButton';
import { usePageTitle } from '../hooks/usePageTitle';
import { navigateTo } from '../lib/navigate';
import { EMAIL_ERROR, isValidEmail } from '../lib/email';

// `?error=` carries a code from a server redirect (Account/CreditCard). Only these messages are
// ever shown; any other value is ignored so a crafted link can't put its own text on this page.
const LOGIN_ERRORS: Record<string, string> = {
  'autologin-failed': 'Auto-login failed. Please login manually.',
};

export default function LoginPage() {
  usePageTitle('Log in');
  const [params] = useSearchParams();
  const returnUrl = params.get('returnUrl') ?? undefined;

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isCourier, setIsCourier] = useState('false');
  const [rememberMe, setRememberMe] = useState(false);
  const [emailError, setEmailError] = useState('');
  const [passwordError, setPasswordError] = useState('');
  const [formError, setFormError] = useState(LOGIN_ERRORS[params.get('error') ?? ''] ?? '');

  const mutation = useMutation({
    mutationFn: login,
    // Full-page navigation: the destination may be the OAuth flow or another app entirely.
    onSuccess: (data) => navigateTo(data.redirectUrl || '/'),
    onError: (error) => setFormError(getApiErrorMessage(error, 'Invalid login attempt.')),
  });
  const busy = mutation.isPending || mutation.isSuccess;

  // Only surface an email error once the user has typed something; empty is left to the submit check.
  const validateEmail = (value: string) => {
    const valid = isValidEmail(value);
    setEmailError(value && !valid ? EMAIL_ERROR : '');
    return valid;
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    const emailOk = validateEmail(email);
    if (!email) setEmailError(EMAIL_ERROR);
    setPasswordError(password ? '' : 'Please enter your password.');
    if (!emailOk || !password) return;
    setFormError('');
    mutation.mutate({ email, password, rememberMe, isCourierLogin: isCourier === 'true', returnUrl });
  };

  return (
    <AuthCard busy={busy}>
      <form onSubmit={onSubmit} noValidate id="loginForm">
        <Stack gap="md">
          <LoginLottie />
          <div>
            <Title order={2} ta="center">
              Welcome back
            </Title>
            <Text c="dimmed" ta="center">
              Sign in to your account
            </Text>
          </div>
          {formError && (
            <Alert color="red" role="alert">
              {formError}
            </Alert>
          )}
          <TextInput
            label="Email"
            type="email"
            placeholder="you@company.com"
            autoComplete="username"
            required
            value={email}
            error={emailError || undefined}
            onChange={(e) => {
              setEmail(e.currentTarget.value);
              if (emailError || e.currentTarget.value) validateEmail(e.currentTarget.value);
            }}
            onBlur={() => validateEmail(email)}
          />
          <PasswordInput
            label="Password"
            placeholder="Enter your password"
            autoComplete="current-password"
            required
            value={password}
            error={passwordError || undefined}
            onChange={(e) => setPassword(e.currentTarget.value)}
          />
          <NativeSelect
            label="Login Type"
            value={isCourier}
            onChange={(e) => setIsCourier(e.currentTarget.value)}
            data={[
              { value: 'false', label: 'Customer Login' },
              { value: 'true', label: 'Courier Login' },
            ]}
          />
          <Center>
            <Checkbox label="Remember me" checked={rememberMe} onChange={(e) => setRememberMe(e.currentTarget.checked)} />
          </Center>
          <Center>
            <SubmitButton id="loginButton" icon={LogIn} label="Log in" busyLabel="Logging in..." busy={busy} />
          </Center>
          <Center>
            <Anchor component={Link} to="/Account/ForgotPassword">
              Forgot your password?
            </Anchor>
          </Center>
        </Stack>
      </form>
    </AuthCard>
  );
}
