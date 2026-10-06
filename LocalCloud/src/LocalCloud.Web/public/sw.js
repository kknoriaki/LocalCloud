// Cache only the application shell. Never cache personal originals, API responses or credentials.
const CACHE = "localcloud-shell-v1";
self.addEventListener("install", (e) =>
  e.waitUntil(
    caches
      .open(CACHE)
      .then((c) => c.addAll(["/", "/icon-192.png", "/manifest.webmanifest"])),
  ),
);
self.addEventListener("activate", (e) =>
  e.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.all(
          keys.filter((k) => k !== CACHE).map((k) => caches.delete(k)),
        ),
      ),
  ),
);
self.addEventListener("fetch", (e) => {
  if (
    e.request.method !== "GET" ||
    new URL(e.request.url).origin !== self.location.origin ||
    /\/(api|uploads|hubs)\//.test(new URL(e.request.url).pathname)
  )
    return;
  e.respondWith(
    fetch(e.request)
      .then((r) => {
        if (
          r.ok &&
          (e.request.mode === "navigate" ||
            new URL(e.request.url).pathname.startsWith("/assets/"))
        ) {
          const copy = r.clone();
          caches.open(CACHE).then((c) => c.put(e.request, copy));
        }
        return r;
      })
      .catch(() =>
        caches
          .match(e.request)
          .then(
            (r) =>
              r ||
              (e.request.mode === "navigate"
                ? caches.match("/")
                : Response.error()),
          ),
      ),
  );
});
