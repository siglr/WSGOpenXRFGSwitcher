'use strict';
(async () => {
  const status = document.getElementById('release-hash');
  const copy = document.getElementById('copy-hash');
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 8000);
  try {
    const response = await fetch('WSG-OpenXR-FG-Switcher.version.json', {cache: 'no-store', signal: controller.signal});
    if (!response.ok) throw new Error('Release unavailable');
    const release = await response.json();
    if (release.schema !== 1 || release.product !== 'WSG-OpenXR-FG-Switcher' || !/^\d+\.\d+\.\d+$/.test(release.version) || !/^[a-f0-9]{64}$/i.test(release.sha256) || !Number.isSafeInteger(release.size) || release.size < 1) throw new Error('Invalid release');
    const hash = release.sha256.toLowerCase();
    document.getElementById('release-version').textContent = 'Development preview · Version ' + release.version;
    document.getElementById('release-size').textContent = (release.size / 1048576).toFixed(1) + ' MB ZIP';
    status.textContent = hash;
    copy.disabled = false;
    copy.addEventListener('click', async () => {
      try { await navigator.clipboard.writeText(hash); document.getElementById('copy-status').textContent = 'Hash copied.'; }
      catch { document.getElementById('copy-status').textContent = 'Select the hash above to copy it manually.'; }
    });
  } catch {
    status.textContent = 'Release information is temporarily unavailable. Please try again later.';
  } finally { clearTimeout(timeout); }
})();
