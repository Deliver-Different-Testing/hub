import { useEffect, useState, type SubmitEvent } from 'react';
import { Alert, Anchor, Center, Stack, Text, TextInput, Title } from '@mantine/core';
import { useMutation, useQuery } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { Link } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { forgotPassword, getConfig } from '../api/hub';
import AuthCard from '../components/AuthCard';
import SubmitButton from '../components/SubmitButton';
import { usePageTitle } from '../hooks/usePageTitle';
import { EMAIL_ERROR, isValidEmail } from '../lib/email';
import { getRecaptchaToken, loadRecaptcha } from '../lib/recaptcha';

const SUCCESS_MESSAGE = 'Password reset instructions have been sent to your email.';
const FAILURE_MESSAGE = 'An error occurred. Please try again later.';

export default function ForgotPasswordPage() {
  usePageTitle('Forgot Password');
  const [email, setEmail] = useState('');
  const [emailError, setEmailError] = useState('');
  const [message, setMessage] = useState<{ text: string; isError: boolean } | null>(null);
  const { data: config } = useQuery({ queryKey: ['config'], queryFn: getConfig, staleTime: Infinity, retry: false });
  const siteKey = config?.recaptchaSiteKey ?? null;

  // Warm the script so the token is ready by the time the form is submitted.
  useEffect(() => {
    if (siteKey) loadRecaptcha(siteKey).catch(() => undefined);
  }, [siteKey]);

  const mutation = useMutation({
    mutationFn: async (address: string) => {
      if (!siteKey) throw new Error('reCAPTCHA is not configured');
      const recaptchaToken = await getRecaptchaToken(siteKey, 'forgot_password');
      await forgotPassword({ email: address, recaptchaToken });
    },
    onSuccess: () => {
      setMessage({ text: SUCCESS_MESSAGE, isError: false });
      setEmail('');
      setEmailError('');
    },
    onError: (error) => setMessage({ text: getApiErrorMessage(error, FAILURE_MESSAGE), isError: true }),
  });

  const onSubmit = (e: SubmitEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!isValidEmail(email)) {
      setEmailError(EMAIL_ERROR);
      return;
    }
    setMessage(null);
    mutation.mutate(email);
  };

  return (
    <AuthCard>
      <form onSubmit={onSubmit} noValidate id="forgot-password-form">
        <Stack gap="md">
          <div>
            <Title order={2}>Forgot Password</Title>
            <Text c="dimmed">Enter your email to reset your password.</Text>
          </div>
          {message && (
            <Alert id="messageArea" role="alert" color={message.isError ? 'red' : 'green'}>
              {message.text}
            </Alert>
          )}
          <TextInput
            label="Email"
            type="email"
            placeholder="Enter your email"
            autoComplete="email"
            required
            value={email}
            error={emailError || undefined}
            onChange={(e) => {
              const value = e.currentTarget.value;
              setEmail(value);
              setEmailError(value && !isValidEmail(value) ? EMAIL_ERROR : '');
            }}
          />
          <Center>
            <SubmitButton id="submitButton" icon={KeyRound} label="Reset Password" busyLabel="Processing..." busy={mutation.isPending} />
          </Center>
          <Anchor component={Link} to="/Account/Login">
            Back to Login
          </Anchor>
        </Stack>
      </form>
    </AuthCard>
  );
}
