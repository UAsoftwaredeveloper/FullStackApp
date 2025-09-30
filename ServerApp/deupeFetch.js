// dedupeFetch.js
const inFlight = new Map();

export function dedupeFetch(url, options) {
  const key = url + (options && JSON.stringify(options) || '');
  if (inFlight.has(key)) return inFlight.get(key);

  const p = fetch(url, options)
    .finally(() => inFlight.delete(key));

  inFlight.set(key, p);
  return p;
}

async function fetchWithEtag(url) {
  const stored = sessionStorage.getItem(`etag:${url}`);
  const headers = {};
  if (stored) headers['If-None-Match'] = stored;

  const res = await fetch(url, { headers });

  if (res.status === 304) {
    // client already has the latest; return cached body from your local cache
    return JSON.parse(sessionStorage.getItem(`body:${url}`));
  }

  const body = await res.json();
  const etag = res.headers.get('ETag') || res.headers.get('etag');
  if (etag) {
    sessionStorage.setItem([etag:${url}](http://_vscodecontentref_/7), etag);
    sessionStorage.setItem(`body:${url}`, JSON.stringify(body));
  }
  return body;
}