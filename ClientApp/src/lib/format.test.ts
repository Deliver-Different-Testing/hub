import { describe, expect, it } from 'vitest';
import { displayNameFromEmail, formatHeaderDate, formatPercent, formatPump, formatShortDate } from './format';

describe('format', () => {
  it('formats the header date from the wall-clock parts without any zone conversion', () => {
    expect(formatHeaderDate('2026-10-27T09:00:00')).toBe('Tuesday, 27 October');
    expect(formatHeaderDate('2026-01-01T00:00:00')).toBe('Thursday, 1 January');
    expect(formatHeaderDate('2026-12-31T23:59:59')).toBe('Thursday, 31 December');
    expect(formatHeaderDate('garbage')).toBe('');
  });

  it('formats short dates and shows Open for none', () => {
    expect(formatShortDate('2026-09-05T00:00:00')).toBe('5 Sep 2026');
    expect(formatShortDate(null)).toBe('Open');
    expect(formatShortDate('nope')).toBe('Open');
  });

  it('scales a fraction to a percent with two decimals', () => {
    expect(formatPercent(0.1234)).toBe('12.34%');
    expect(formatPercent(0.1)).toBe('10.00%');
    expect(formatPercent(null)).toBe('—');
  });

  it('formats pump price to four decimals', () => {
    expect(formatPump(2.5)).toBe('2.5000');
    expect(formatPump(undefined)).toBe('—');
  });

  it('derives a display name from the email local part', () => {
    expect(displayNameFromEmail('jane.doe@example.com')).toBe('Jane Doe');
    expect(displayNameFromEmail('JOHN_SMITH-jr@x.com')).toBe('John Smith Jr');
    expect(displayNameFromEmail('@x.com')).toBe('@x.com');
  });
});
