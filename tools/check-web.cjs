// Local-only browser review. Never starts WSG, a provider or a game.
const { chromium } = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const crypto = require('node:crypto');
const assert = require('node:assert/strict');
const project = path.resolve(__dirname, '..');
const site = path.join(project, 'dist', 'website');
const output = path.join(project, 'build', 'web-review');
fs.mkdirSync(output, { recursive: true });
const shared = 'C:\\WSG Stream Feeder\\web';
const glass = 'C:\\Standalone-DLSS-Glass-Cockpit-Refresh\\standalone\\site\\Standalone-DLSS-Glass-Cockpit-Refresh.html';
const types = {'.html':'text/html; charset=utf-8','.css':'text/css','.js':'text/javascript','.json':'application/json','.png':'image/png','.zip':'application/zip'};
const server = http.createServer((req, res) => {
  const name = decodeURIComponent(new URL(req.url, 'http://localhost').pathname).slice(1) || 'index.html';
  let file = path.resolve(site, name);
  if (!file.startsWith(site + path.sep)) { res.writeHead(403).end(); return; }
  if (name === 'WSG-Stream-Feeder.html' || name === 'feeder.js') file = path.join(shared, name);
  if (name === 'Standalone-DLSS-Glass-Cockpit-Refresh.html') file = glass;
  if (!fs.existsSync(file) || !fs.statSync(file).isFile()) { res.writeHead(404).end(); return; }
  res.writeHead(200, {'Content-Type':types[path.extname(file)] || 'application/octet-stream'});
  fs.createReadStream(file).pipe(res);
});
(async () => {
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}/`;
  const browser = await chromium.launch({executablePath:'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', headless:true});
  const checks = [];
  try {
    const context = await browser.newContext({permissions:['clipboard-read','clipboard-write']});
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    for (const [label, width] of [['desktop',1440],['tablet',768],['mobile',390]]) {
      await page.setViewportSize({width, height:900});
      for (const name of ['index.html','WSG-OpenXR-FG-Switcher.html']) {
        await page.goto(base + name);
        if (name !== 'index.html') await page.waitForFunction(() => /^[a-f0-9]{64}$/.test(document.getElementById('release-hash').textContent));
        await page.evaluate(() => Promise.all([...document.images].map(i => i.decode())));
        assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${name} overflows at ${width}`);
        assert(await page.locator('h1').count() === 1);
        for (const href of await page.locator('a[href^="#"]').evaluateAll(links => links.map(l => l.getAttribute('href')))) assert(await page.locator(href).count() === 1, href);
        if (name === 'index.html') assert.equal(await page.locator('.tool').count(), 3);
        await page.screenshot({path:path.join(output, name.replace('.html','') + '-' + label + '.png'), fullPage:true});
        checks.push(`${name}: ${width}px, images loaded, no horizontal overflow, section links valid`);
      }
    }
    const release = JSON.parse(fs.readFileSync(path.join(site,'WSG-OpenXR-FG-Switcher.version.json')));
    const response = await context.request.get(base + 'WSG-OpenXR-FG-Switcher.zip');
    assert(response.ok());
    const bytes = await response.body();
    assert.equal(bytes.length, release.size);
    assert.equal(crypto.createHash('sha256').update(bytes).digest('hex'), release.sha256);
    assert.equal(await page.locator('details[open]').count(), 0);
    await page.locator('#verify > summary').focus();
    await page.keyboard.press('Space');
    assert(await page.locator('#copy-hash').isVisible());
    assert.equal(await page.locator('#release-hash').innerText(), release.sha256);
    await page.locator('#copy-hash').click();
    assert.equal(await page.evaluate(() => navigator.clipboard.readText()), release.sha256);
    checks.push('Download bytes, size, displayed checksum and clipboard match signed package metadata');
    await page.locator('#verify > summary').click();
    for (const detail of await page.locator('details').all()) {
      await detail.locator('summary').click();
      assert(await detail.evaluate(el => el.open));
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Expanded details overflow on mobile');
      await detail.locator('summary').click();
    }
    checks.push('Technical details start collapsed, open by keyboard/click and fit mobile width');
    await page.locator('summary').filter({hasText:'What happens when I close'}).click();
    assert.equal(await page.locator('details[open]').count(), 1);
    await page.getByRole('link',{name:'All tools',exact:true}).click();
    for (const href of await page.locator('.tool-copy .button').evaluateAll(links => links.map(l=>l.getAttribute('href')))) assert((await context.request.get(base+href)).ok(), href);
    await page.getByRole('link', {name:'Details & download →'}).last().click();
    assert(page.url().endsWith('WSG-OpenXR-FG-Switcher.html'));
    checks.push('FAQ expands, All tools returns to index, all three detail-page links resolve');
    await page.route('**/WSG-OpenXR-FG-Switcher.version.json', route => route.fulfill({json:{...release,product:'wrong-product'}}));
    await page.reload();
    await page.waitForFunction(() => document.getElementById('release-hash').textContent.includes('unavailable'));
    assert(await page.locator('#copy-hash').isDisabled());
    checks.push('Wrong-product release metadata rejected; checksum copy stays disabled');
    await page.unroute('**/WSG-OpenXR-FG-Switcher.version.json');
    await page.route('**/WSG-OpenXR-FG-Switcher.version.json', route => route.fulfill({status:404,body:'missing'}));
    await page.reload();
    await page.waitForFunction(() => document.getElementById('release-hash').textContent.includes('unavailable'));
    assert(await page.locator('#copy-hash').isDisabled());
    checks.push('Unavailable metadata displays an honest fallback');
    const noJs = await browser.newContext({javaScriptEnabled:false,viewport:{width:390,height:844}});
    const staticPage = await noJs.newPage();
    await staticPage.goto(base+'WSG-OpenXR-FG-Switcher.html');
    await staticPage.locator('#verify > summary').click();
    assert(await staticPage.locator('noscript').isVisible());
    assert(await staticPage.locator('a[download]').isVisible());
    checks.push('Without JavaScript: download, instructions and metadata link remain available');
    assert.deepEqual(errors, []);
    fs.writeFileSync(path.join(output,'checks.json'),JSON.stringify({passed:true,checks},null,2));
    console.log(checks.join('\n'));
  } finally {
    await browser.close();
    await new Promise(resolve => server.close(resolve));
  }
})().catch(error => { console.error(error); server.close(); process.exitCode=1; });
