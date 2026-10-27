import { useRef, useState, type SubmitEvent } from 'react';
import { Alert, Anchor, Center, Loader, PasswordInput, Progress, Stack, Text, Title } from '@mantine/core';
import { useMutation, useQuery } from '@tanstack/react-query';
import { KeyRound } from 'lucide-react';
import { Link, useSearchParams } from 'react-router-dom';
import { getApiErrorMessage } from '../api/client';
import { resetPassword, validateResetCode } from '../api/hub';
import AuthCard from '../components/AuthCard';
import SubmitButton from '../components/SubmitButton';
import { usePageTitle } from '../hooks/usePageTitle';
import { navigateTo } from '../lib/navigate';
import {
  CONFIRM_MISMATCH_MESSAGE,
  MIN_STRENGTH_SCORE,
  PASSWORD_INVALID_MESSAGE,
  STRENGTH_LABELS,
  WEAK_PASSWORD_MESSAGE,
  isStrongPassword,
  loadZxcvbn,
  scorePassword,
} from '../lib/passwordStrength';

const METER_COLORS = ['red', 'orange', 'yellow', 'green', 'green'] as const;
const INVALID_LINK = 'This password reset link is invalid or has expired.';

interface Scored {
  value: string;
  score: number;
  feedback: string;
}

export default function ResetPasswordPage() {
  usePageTitle('Reset Password');
  const code = useSearchParams()[0].get('code') ?? '';
  const { data, isPending, isError } = useQuery({
    queryKey: ['reset-code', code],
    queryFn: () => validateResetCode(code),
    enabled: code !== '',
    retry: false,
    staleTime: Infinity,
  });

  return (
    <AuthCard>
      {!code || isError ? (
        <Stack gap="md">
          <Title order={2}>Reset Password</Title>
          <Alert color="red" role="alert">
            {INVALID_LINK}
          </Alert>
          <Anchor component={Link} to="/Account/ForgotPassword">
            Request a new link
          </Anchor>
        </Stack>
      ) : isPending ? (
        <Center>
          <Loader aria-label="Checking reset link" />
        </Center>
      ) : (
        <ResetForm code={code} email={data.email} />
      )}
    </AuthCard>
  );
}

function ResetForm({ code, email }: { code: string; email: string }) {
  const [password, setPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [scored, setScored] = useState<Scored | null>(null);
  const [submitted, setSubmitted] = useState(false);
  const [formError, setFormError] = useState('');
  const latest = useRef('');

  const mutation = useMutation({
    mutationFn: resetPassword,
    onSuccess: (r) => navigateTo(r.redirectUrl || '/'),
    onError: (error) => setFormError(getApiErrorMessage(error, 'Could not reset your password. Please try again.')),
  });
  const busy = mutation.isPending || mutation.isSuccess;

  const onPasswordChange = (value: string) => {
    setPassword(value);
    latest.current = value;
    if (!value) return setScored(null);
    // Bails if the value changed while the estimator was loading.
    void scorePassword(value).then((r) => {
      if (latest.current === value) setScored({ value, ...r });
    });
  };

  // Composition first; strength only fails the field once a score exists for the current value.
  let passwordError = '';
  if (password) {
    if (!isStrongPassword(password)) passwordError = PASSWORD_INVALID_MESSAGE;
    else if (scored?.value === password && scored.score < MIN_STRENGTH_SCORE) passwordError = WEAK_PASSWORD_MESSAGE;
  } else if (submitted) {
    passwordError = 'Please enter a new password.';
  }
  let confirmError = '';
  if (confirm && confirm !== password) confirmError = CONFIRM_MISMATCH_MESSAGE;
  else if (!confirm && submitted) confirmError = 'Please confirm your password.';

  const onSubmit = async (e: SubmitEvent<HTMLFormElement>) => {
    e.preventDefault();
    setSubmitted(true);
    if (!password || !confirm || !isStrongPassword(password) || password !== confirm) return;
    // Composition passed: make sure the strength gate is satisfied before the POST.
    const result = await scorePassword(password);
    setScored({ value: password, ...result });
    if (result.score < MIN_STRENGTH_SCORE) return;
    setFormError('');
    mutation.mutate({ code, password, confirmPassword: confirm });
  };

  return (
    <form onSubmit={(e) => void onSubmit(e)} noValidate id="resetPasswordForm">
      <Stack gap="md">
        <div>
          <Title order={2}>Reset Password</Title>
          <Text c="dimmed">
            Reset your password for <strong>{email}</strong>
          </Text>
        </div>
        {formError && (
          <Alert color="red" role="alert">
            {formError}
          </Alert>
        )}
        <div>
          <PasswordInput
            label="New Password"
            placeholder="Enter new password"
            autoComplete="new-password"
            required
            minLength={8}
            value={password}
            error={passwordError || undefined}
            onFocus={() => void loadZxcvbn()}
            onChange={(e) => onPasswordChange(e.currentTarget.value)}
          />
          {scored && password && (
            <div id="passwordStrength" data-score={scored.score}>
              <Progress
                mt="xs"
                size="sm"
                aria-label="Password strength"
                value={((scored.score + 1) / STRENGTH_LABELS.length) * 100}
                color={METER_COLORS[scored.score]}
              />
              <Text size="sm" fw={500} aria-live="polite" id="strengthLabel">
                {STRENGTH_LABELS[scored.score]}
              </Text>
              <Text size="xs" c="dimmed" aria-live="polite" id="strengthFeedback">
                {scored.feedback}
              </Text>
            </div>
          )}
        </div>
        <PasswordInput
          label="Confirm Password"
          placeholder="Re-enter your password"
          autoComplete="new-password"
          required
          value={confirm}
          error={confirmError || undefined}
          onChange={(e) => setConfirm(e.currentTarget.value)}
        />
        <Center>
          <SubmitButton id="resetButton" icon={KeyRound} label="Reset Password" busyLabel="Processing..." busy={busy} />
        </Center>
      </Stack>
    </form>
  );
}
