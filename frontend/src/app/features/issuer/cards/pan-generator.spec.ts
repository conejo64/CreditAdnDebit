import { generatePan, isLuhnValid } from './pan-generator';

/**
 * Card issuance builds a PAN from the selected BIN before sending it to CardVault.
 *
 * Regression guard. The BIN arrives from the catalog API as a JSON number
 * (BinRangeEntity.BinStart is an int), and the previous inline implementation did
 * `let pan = bin` followed by `while (pan.length < 15)`. On a number, `.length` is
 * undefined, the loop never ran, and the PAN came out 7 characters long instead of 16.
 * The old code also appended a random final digit, so the PAN failed the Luhn check
 * that every real card number must satisfy.
 */
describe('generatePan', () => {
  const BIN = '438108';

  it('returns a 16-digit PAN', () => {
    expect(generatePan(BIN).length).toBe(16);
  });

  it('starts with the requested BIN', () => {
    expect(generatePan(BIN).startsWith(BIN)).toBe(true);
  });

  it('contains only digits', () => {
    expect(generatePan(BIN)).toMatch(/^\d{16}$/);
  });

  it('produces a PAN that satisfies the Luhn check', () => {
    for (let i = 0; i < 200; i++) {
      const pan = generatePan(BIN);
      expect(isLuhnValid(pan))
        .withContext(`generated PAN ${pan} must pass the Luhn check`)
        .toBe(true);
    }
  });

  // RED before the fix: a numeric BIN produced a 7-character PAN.
  it('accepts a numeric BIN and still returns 16 digits', () => {
    const pan = generatePan(438108 as unknown as string);
    expect(pan).toMatch(/^\d{16}$/);
    expect(pan.startsWith('438108')).toBe(true);
    expect(isLuhnValid(pan)).toBe(true);
  });
});

describe('isLuhnValid', () => {
  it('accepts known-valid test card numbers', () => {
    // Standard published test PANs, all Luhn-valid.
    expect(isLuhnValid('4111111111111111')).toBe(true);
    expect(isLuhnValid('5500005555555559')).toBe(true);
    expect(isLuhnValid('4000056655665556')).toBe(true);
  });

  it('rejects a number whose check digit is wrong', () => {
    expect(isLuhnValid('4111111111111112')).toBe(false);
  });

  it('rejects non-digit input', () => {
    expect(isLuhnValid('4111-1111-1111-1111')).toBe(false);
    expect(isLuhnValid('')).toBe(false);
  });
});
