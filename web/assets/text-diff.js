// Word-level diff for description text. Tokens are whitespace-separated words (so "5%" or
// "3.5" stay one word, never split into digit/punctuation pieces) - whitespace itself is NOT
// a token. Diffing raw whitespace runs as their own LCS tokens looked correct in theory but
// broke in practice: every space token is the string " ", so the LCS backtrack is free to
// match ANY old space to ANY new space while still maximizing total match count, and often
// picks a pairing far from the actual edit - leaving two genuinely different adjacent words
// glued together with no space between them ("yourall"). Diffing words only and letting the
// template insert one literal space between rendered segments sidesteps that entirely.
export function diffWords(oldText, newText) {
    const a = oldText.split(/\s+/).filter(Boolean);
    const b = newText.split(/\s+/).filter(Boolean);

    const n = a.length, m = b.length;
    const lcs = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
    for (let i = n - 1; i >= 0; i--) {
        for (let j = m - 1; j >= 0; j--) {
            lcs[i][j] = a[i] === b[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
        }
    }

    const ops = [];
    let i = 0, j = 0;
    while (i < n && j < m) {
        if (a[i] === b[j]) {
            ops.push({ type: 'equal', text: a[i] });
            i++; j++;
        } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
            ops.push({ type: 'removed', text: a[i] });
            i++;
        } else {
            ops.push({ type: 'added', text: b[j] });
            j++;
        }
    }
    while (i < n) { ops.push({ type: 'removed', text: a[i] }); i++; }
    while (j < m) { ops.push({ type: 'added', text: b[j] }); j++; }

    // Merge consecutive same-type ops into one segment (words joined by a single space) so
    // a run of several changed/unchanged words renders as one span, not one per word.
    const merged = [];
    for (const op of ops) {
        const last = merged[merged.length - 1];
        if (last && last.type === op.type) last.text += ' ' + op.text;
        else merged.push({ ...op });
    }
    return merged;
}
