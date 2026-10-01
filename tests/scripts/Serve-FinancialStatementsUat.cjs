// Read-only production-component fixture harness. Interactions are verified by bUnit.
const http = require('node:http'), fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'docs/verification/financial-statements');
const web = path.join(root, 'src/VirtualCompany.Web');
const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (['/profit-loss', '/balance-sheet'].includes(url.pathname)) {
        const shell = fs.readFileSync(path.join(fixtures, 'shell.html'), 'utf8');
        const component = fs.readFileSync(path.join(fixtures, url.pathname.slice(1) + '.html'), 'utf8');
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
        res.end('<!doctype html><html lang="sv"><head><meta name="viewport" content="width=device-width,initial-scale=1">' +
            '<title>Financial statement — isolated visual fixture</title><link rel="stylesheet" href="/lib/bootstrap/dist/css/bootstrap.min.css">' +
            '<link rel="stylesheet" href="/css/app.css"><link rel="stylesheet" href="/scoped.css"></head><body>' +
            '<div class="page" b-hbjt0rdsei>' + shell + '<main b-hbjt0rdsei><div class="top-row px-4" b-hbjt0rdsei></div>' +
            '<article class="content px-4" b-hbjt0rdsei><section class="py-4 dashboard-hero finance-module-shell">' +
            '<div class="statement-page"><nav class="statement-breadcrumb">Ekonomi <span>/</span> Bokföring <span>/</span> Rapporter</nav>' +
            component + '</div></section></article></main></div></body></html>');
        return;
    }
    const relative = decodeURIComponent(url.pathname).replace(/^\/+/, '');
    const file = url.pathname === '/scoped.css'
        ? path.join(web, 'obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css')
        : path.resolve(web, 'wwwroot', relative);
    if (!file.startsWith(web + path.sep) || !fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': file.endsWith('.css') ? 'text/css' : 'image/png', 'Cache-Control': 'no-store' });
    res.end(fs.readFileSync(file));
});
server.listen(0, '127.0.0.1', () => console.log('Financial statement fixture: http://127.0.0.1:' + server.address().port));
process.on('SIGINT', () => server.close());
