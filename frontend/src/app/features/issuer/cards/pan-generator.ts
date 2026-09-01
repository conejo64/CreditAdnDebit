/**
 * PAN construction for card issuance.
 *
 * The BIN reaches this module from the catalog API, where BinRangeEntity.BinStart is
 * an int and therefore arrives as a JSON number. Every entry point coerces it to a
 * string rather than trusting a TypeScript annotation, which is erased at runtime.
 */

const PAN_LENGTH = 16;

/**
 * Builds a PAN that starts with the given BIN, is exactly 16 digits long, and carries
 * a correct Luhn check digit — the same validation any acquirer or network applies.
 */
export function generatePan(bin: string): string {
    const prefix = String(bin).replace(/\D/g, '').slice(0, PAN_LENGTH - 1);

    let partial = prefix;
    while (partial.length < PAN_LENGTH - 1) {
        partial += Math.floor(Math.random() * 10).toString();
    }

    return partial + luhnCheckDigit(partial);
}

/**
 * Returns the digit that makes `partial` Luhn-valid once appended.
 *
 * The appended digit sits at position 1 from the right, so within `partial` the
 * rightmost digit occupies an even position from the right and must be doubled.
 */
function luhnCheckDigit(partial: string): string {
    let sum = 0;
    let double = true;

    for (let i = partial.length - 1; i >= 0; i--) {
        let digit = partial.charCodeAt(i) - 48;

        if (double) {
            digit *= 2;
            if (digit > 9) {
                digit -= 9;
            }
        }

        sum += digit;
        double = !double;
    }

    return (((10 - (sum % 10)) % 10)).toString();
}

/** Validates a PAN against the Luhn checksum. Non-digit input is never valid. */
export function isLuhnValid(pan: string): boolean {
    if (!/^\d+$/.test(pan)) {
        return false;
    }

    let sum = 0;
    let double = false;

    for (let i = pan.length - 1; i >= 0; i--) {
        let digit = pan.charCodeAt(i) - 48;

        if (double) {
            digit *= 2;
            if (digit > 9) {
                digit -= 9;
            }
        }

        sum += digit;
        double = !double;
    }

    return sum % 10 === 0;
}
