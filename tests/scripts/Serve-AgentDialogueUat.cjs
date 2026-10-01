// Read-only rendering harness. No API, meeting commands, media or browser automation.
// Export current component fixtures with VC_BROWSER_UAT_DIRECTORY before starting.
const http = require('node:http'), fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const fixtures = path.join(root, 'artifacts/agentdialog-prompt6');
const web = path.join(root, 'src/VirtualCompany.Web');
const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    let file;
    if (/^\/host-dialogue-[a-z_]+\.html$/.test(url.pathname) || url.pathname === '/guest-presentation.html') {
        file = path.join(fixtures, path.basename(url.pathname));
        if (!fs.existsSync(file)) { res.writeHead(404); res.end('Export component fixtures first.'); return; }
        res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
        res.end('<!doctype html><html lang="en"><head><meta name="viewport" content="width=device-width,initial-scale=1">' +
            '<title>Agent dialogue component UAT — isolated fixture</title><link rel="stylesheet" href="/app.css">' +
            '<link rel="stylesheet" href="/scoped.css"></head><body>' + fs.readFileSync(file, 'utf8') + '</body></html>');
        return;
    }
    if (url.pathname === '/app.css') file = path.join(web, 'wwwroot/app.css');
    if (url.pathname === '/scoped.css') file = path.join(web, 'obj/Debug/net9.0/scopedcss/bundle/VirtualCompany.Web.styles.css');
    if (!file || !fs.existsSync(file)) { res.writeHead(404); res.end(); return; }
    res.writeHead(200, { 'Content-Type': 'text/css; charset=utf-8', 'Cache-Control': 'no-store' });
    res.end(fs.readFileSync(file));
});
server.listen(0, '127.0.0.1', () => console.log('Fixture UAT origin: http://127.0.0.1:' + server.address().port));
process.on('SIGINT', () => server.close());
