export function downloadReport(name, content) {
    const url = URL.createObjectURL(new Blob(['\ufeff', content], { type: 'text/csv;charset=utf-8' }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    document.body.append(anchor);
    anchor.click();
    anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function printReport() {
    document.body.classList.add('printing-financial-statement');
    const cleanup = () => document.body.classList.remove('printing-financial-statement');
    window.addEventListener('afterprint', cleanup, { once: true });
    try { window.print(); } finally { cleanup(); }
}
