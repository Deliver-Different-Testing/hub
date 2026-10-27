import { describe, expect, it } from 'vitest';
import { isValidEmail } from './email';

// Ports Hub.Tests/Views/EmailValidationRegexTests.cs (the regex is now shared by login and forgot-password).
describe('isValidEmail', () => {
  it.each(['john.doe@email.com', 'first.last@example.co.nz', 'a.b.c@test.com', 'user@example.com', 'test@test.co.nz', 'UPPER@Example.COM'])(
    'accepts %s',
    (email) => expect(isValidEmail(email)).toBe(true),
  );

  it.each(['', 'not-an-email', '@missing-local.com', 'missing-domain@', 'a b@c.com'])('rejects %j', (email) =>
    expect(isValidEmail(email)).toBe(false),
  );
});
