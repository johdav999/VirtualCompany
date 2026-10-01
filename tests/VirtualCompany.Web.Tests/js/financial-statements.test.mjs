import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../../../src/VirtualCompany.Web/wwwroot/js/financialStatements.js', import.meta.url), 'utf8');
const api = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('CSV download preserves exact report content and filename', async () => {
    let blob, downloaded;
    const originalDocument = globalThis.document;
    const originalCreate = URL.createObjectURL, originalRevoke = URL.revokeObjectURL;
    const originalTimeout = globalThis.setTimeout;
    try {
        URL.createObjectURL = value => { blob = value; return 'blob:report'; };
        URL.revokeObjectURL = value => assert.equal(value, 'blob:report');
        globalThis.setTimeout = callback => callback();
        globalThis.document = { body: { append() {} }, createElement: () => ({ click() { downloaded = this.download; }, remove() {} }) };
        api.downloadReport('profit-loss-retained.csv', 'Snapshot,abc\nPeriodens resultat,560000.00');
        assert.equal(downloaded, 'profit-loss-retained.csv');
        assert.equal(await blob.text(), 'Snapshot,abc\nPeriodens resultat,560000.00');
        assert.equal(blob.type, 'text/csv;charset=utf-8');
    } finally {
        globalThis.document = originalDocument; globalThis.setTimeout = originalTimeout;
        URL.createObjectURL = originalCreate; URL.revokeObjectURL = originalRevoke;
    }
});

test('Print applies report-only mode and cleans up when the browser rejects printing', () => {
    const originalDocument = globalThis.document, originalWindow = globalThis.window;
    const classes = new Set();
    try {
        globalThis.document = { body: { classList: { add: c => classes.add(c), remove: c => classes.delete(c) } } };
        globalThis.window = { addEventListener() {}, print() { assert.ok(classes.has('printing-financial-statement')); throw new Error('print failed'); } };
        assert.throws(() => api.printReport(), /print failed/);
        assert.equal(classes.size, 0);
    } finally { globalThis.document = originalDocument; globalThis.window = originalWindow; }
});
