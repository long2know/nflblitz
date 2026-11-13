const CACHE_NAME = 'nflblitz-verified-content-v1';

export function createVerifiedAssetLoader({
  baseUrl, fetchBytes, digest, getStorage = () => globalThis.caches,
  warn = message => console.warn(message),
}) {
  const base = new URL('./', baseUrl);
  if (!['http:', 'https:'].includes(base.protocol) || typeof fetchBytes !== 'function' || typeof digest !== 'function')
    throw new Error('Verified asset loading requires an HTTP base, fetch and SHA-256 verifier.');
  const counts = { cached: 0, downloaded: 0, cachedBytes: 0, downloadedBytes: 0, corruptEntries: 0 };
  let opened, disabled = false, warned = false;

  function unavailable(error) {
    disabled = true;
    if (!warned) {
      warned = true;
      warn(`Verified asset cache unavailable (${error?.name || 'CacheStorage'}: ${error?.message || error}); ` +
        'continuing with integrity-checked network downloads.');
    }

  }

  async function cache() {
    if (disabled) return null;
    opened ??= (async () => {
      try {
        const storage = getStorage();
        if (!storage?.open) throw new Error('Persistent CacheStorage is unsupported or disabled.');
        return await storage.open(CACHE_NAME);
      } catch (error) {
        unavailable(error);
        return null;
      }
    })();
    return opened;
  }

  async function valid(bytes, file) {
    return bytes.length === file.bytes && await digest(bytes) === file.sha256;
  }

  async function load(file) {
    if (!file || !/^[A-Za-z0-9_.\-/]+$/.test(file.path) ||
        file.path.split('/').some(part => !part || part === '.' || part === '..') ||
        !Number.isSafeInteger(file.bytes) || file.bytes <= 0 || !/^[0-9a-f]{64}$/.test(file.sha256))
      throw new Error(`Unsafe or unpinned verified asset: ${file?.path}`);
    const key = new URL(`.verified-assets/${file.sha256}`, base).href;
    const store = await cache();
    let response, cached;
    if (store && !disabled) {
      try { response = await store.match(key); } catch (error) { unavailable(error); }
      if (response) {
        try { cached = new Uint8Array(await response.arrayBuffer()); } catch (error) { unavailable(error); }
        if (cached && await valid(cached, file)) {
          counts.cached++; counts.cachedBytes += cached.length;
          return cached;
        }
        if (cached) {
          counts.corruptEntries++;
          warn(`Verified asset cache entry failed integrity and was discarded: ${file.path}`);
          try { await store.delete(key); } catch (error) { unavailable(error); }
        }
      }
    }
    const bytes = await fetchBytes(file.path);
    if (!(bytes instanceof Uint8Array) || !await valid(bytes, file))
      throw new Error(`Actual original asset content pin mismatch: ${file.path}`);
    counts.downloaded++; counts.downloadedBytes += bytes.length;
    if (store && !disabled) {
      try {
        await store.put(key, new Response(bytes.slice(), {
          headers: { 'Content-Type': 'application/octet-stream', 'X-Verified-SHA256': file.sha256 },
        }));
      } catch (error) { unavailable(error); }
    }
    return bytes;
  }

  return Object.freeze({
    load,
    snapshot: () => Object.freeze({ ...counts, persistentCacheAvailable: !disabled }),
    baseUrl: base.href,
  });
}

export function createVerifiedRuntimeResourceLoader({ baseUrl, resources, load }) {
  const base = new URL('./', baseUrl);
  if (resources?.schema !== 'source-verified-runtime-resources/v1' || !Array.isArray(resources.files))
    throw new Error('Unknown verified runtime resource inventory.');
  const pins = new Map();
  for (const file of resources.files) {
    if (!/^_framework\/[A-Za-z0-9_.-]+\.(wasm|dat)$/.test(file.path) ||
        !Number.isSafeInteger(file.bytes) || file.bytes <= 0 ||
        !/^[0-9a-f]{64}$/.test(file.sha256) || pins.has(file.path))
      throw new Error(`Unsafe/duplicate/unpinned runtime resource: ${file.path}`);
    pins.set(file.path, file);
  }
  return (_type, _name, defaultUri, integrity) => {
    const url = new URL(defaultUri, base);
    if (!/\.(wasm|dat)$/.test(url.pathname)) return null;
    if (url.origin !== base.origin || !url.pathname.startsWith(base.pathname) || url.search || url.hash)
      throw new Error('Runtime resource escaped the verified application base.');
    const path = url.pathname.slice(base.pathname.length), file = pins.get(path);
    if (!file) throw new Error(`Required runtime resource has no content pin: ${path}`);
    const expected = 'sha256-' + btoa(String.fromCharCode(...file.sha256.match(/../g).map(byte => parseInt(byte, 16))));
    if (integrity && integrity !== expected) throw new Error(`Runtime boot integrity differs from build receipt: ${path}`);
    return load(file).then(bytes => new Response(bytes, {
      headers: { 'Content-Type': path.endsWith('.wasm') ? 'application/wasm' : 'application/octet-stream' },
    }));
  };
}
