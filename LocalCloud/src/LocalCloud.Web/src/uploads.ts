import { Upload } from "tus-js-client";
import { api } from "./api";
import { useSyncExternalStore } from "react";
export interface Task {
  id: string;
  file: File;
  folder: string;
  bytes: number;
  total: number;
  confirmed: number;
  reconnecting?: boolean;
  speed: number;
  status:
    | "waiting"
    | "uploading"
    | "paused"
    | "verifying"
    | "duplicate"
    | "complete"
    | "skipped"
    | "error"
    | "cancelled";
  error?: string;
  duplicatePath?: string;
  upload?: Upload;
  serverId?: string;
  abortFinalize?: boolean;
  policy: string;
}
let queue: Task[] = [];
let snapshot: Task[] = [];
const listeners = new Set<() => void>();
let concurrency = 2;
let defaultPolicy = "ask";
let skipAll = false;
function notify() {
  snapshot = [...queue];
  listeners.forEach((l) => l());
}
export function useUploads() {
  return useSyncExternalStore(
    (cb) => {
      listeners.add(cb);
      return () => {
        listeners.delete(cb);
      };
    },
    () => snapshot,
  );
}
export function configureUploads(count: number, policy: string) {
  concurrency = count;
  defaultPolicy = policy;
  pump();
}
function pump() {
  let active = queue.filter(
    (t) => t.status === "uploading" || t.status === "verifying",
  ).length;
  for (const t of queue) {
    if (active >= concurrency) break;
    if (t.status === "waiting") {
      active++;
      void start(t);
    }
  }
}
async function forget(t: Task) {
  if (!t.upload) return;
  try {
    for (const prev of await t.upload.findPreviousUploads())
      await t.upload.options.urlStorage?.removeUpload(prev.urlStorageKey);
  } catch {
    /* Private browsing may disable persistent URL storage. */
  }
}
async function finalize(t: Task, policy: string) {
  t.status = "verifying";
  notify();
  try {
    const result = await api<{
      status: Task["status"];
      path?: string;
      resultId?: string;
    }>("/uploads/" + t.serverId + "/finalize", { policy });
    t.status = result.status;
    t.duplicatePath = result.path;
    t.speed = 0;
    if (t.status === "complete" || t.status === "skipped") {
      await forget(t);
      window.dispatchEvent(new Event("lc-refresh"));
    }
  } catch (e) {
    t.status = "error";
    t.error = (e as Error).message;
  }
  notify();
  pump();
}
async function start(t: Task) {
  t.status = "uploading";
  t.error = undefined;
  notify();
  let lastBytes = t.bytes,
    lastTime = performance.now();
  if (t.serverId && t.confirmed === t.total) {
    void finalize(t, skipAll ? "skip" : t.policy);
    return;
  }
  if (t.upload) {
    t.reconnecting = false;
    t.upload.start();
    return;
  }
  t.upload = new Upload(t.file, {
    endpoint: "/uploads",
    chunkSize: 8 * 1024 * 1024,
    retryDelays: [0, 1000, 3000, 5000, 10000, 20000],
    metadata: {
      filename: t.file.name,
      filetype: t.file.type,
      folder: t.folder,
    },
    removeFingerprintOnSuccess: false,
    onBeforeRequest: (req) => {
      // tus 4.x listens to XHR error, but not timeout. Forward timeout into the standard retry path.
      const xhr = req.getUnderlyingObject() as XMLHttpRequest;
      xhr.timeout = 60000;
      xhr.addEventListener(
        "timeout",
        () => xhr.dispatchEvent(new ProgressEvent("error")),
        { once: true },
      );
    },
    onAfterResponse: (req, res) => {
      if (req.getMethod() === "HEAD" || req.getMethod() === "PATCH") {
        const offset = Number(res.getHeader("Upload-Offset"));
        if (
          res.getStatus() >= 200 &&
          res.getStatus() < 300 &&
          Number.isFinite(offset)
        )
          t.confirmed = offset;
      }
    },
    onShouldRetry: (err) => {
      const code = err.originalResponse?.getStatus() || 0;
      const retry =
        code === 0 ||
        code === 409 ||
        code === 423 ||
        code === 429 ||
        code >= 500;
      if (retry && t.status === "uploading") {
        t.reconnecting = true;
        t.speed = 0;
        t.bytes = t.confirmed;
        notify();
      }
      return retry;
    },
    onChunkComplete: (_, accepted) => {
      t.confirmed = accepted;
      t.serverId = t.upload?.url?.split("/").pop();
      t.reconnecting = false;
      notify();
    },
    fingerprint: async (f) =>
      `localcloud-${location.origin}-${t.folder}-${f.name}-${f.size}-${(f as File).lastModified}`,
    onProgress: (sent, total) => {
      const now = performance.now();
      t.bytes = sent;
      t.reconnecting = false;
      t.total = total;
      if (now - lastTime > 400) {
        t.speed = Math.max(0, ((sent - lastBytes) * 1000) / (now - lastTime));
        lastTime = now;
        lastBytes = sent;
      }
      notify();
    },
    onError: (e) => {
      if (t.status === "paused" || t.status === "cancelled") return;
      t.status = "error";
      t.error = navigator.onLine
        ? "Загрузка прервана. Нажмите «Повторить», чтобы продолжить."
        : "Нет соединения. Возобновите загрузку после подключения к Wi-Fi.";
      console.warn(e.message);
      notify();
      pump();
    },
    onSuccess: () => {
      t.serverId = t.upload?.url?.split("/").pop();
      void finalize(t, skipAll ? "skip" : t.policy);
    },
  });
  try {
    const previous = await t.upload.findPreviousUploads().catch(() => []);
    if (previous.length && t.status === "uploading")
      t.upload.resumeFromPreviousUpload(previous[0]);
    if (t.status === "uploading") t.upload.start();
  } catch (e) {
    t.status = "error";
    t.error = (e as Error).message;
    notify();
    pump();
  }
}
export function addUploads(files: File[], folder: string) {
  for (const file of files)
    queue.push({
      id: crypto.randomUUID?.() || Math.random().toString(36),
      file,
      folder,
      bytes: 0,
      confirmed: 0,
      total: file.size,
      speed: 0,
      status: "waiting",
      policy: defaultPolicy,
    });
  notify();
  pump();
}
export async function uploadAction(id: string, action: string) {
  const t = queue.find((t) => t.id === id);
  if (!t) return;
  if (action === "pause") {
    if (t.status !== "uploading") return;
    t.status = "paused";
    await t.upload?.abort();
    t.speed = 0;
    notify();
    pump();
  } else if (action === "resume") {
    if (!["paused", "error"].includes(t.status)) return;
    t.status = "waiting";
    notify();
    pump();
  } else if (action === "cancel") {
    t.status = "cancelled";
    await t.upload?.abort();
    const server = t.serverId || t.upload?.url?.split("/").pop();
    if (server) {
      try {
        await api("/uploads/" + server, undefined, "DELETE");
      } catch {
        t.status = "error";
        t.error =
          "Не удалось отменить загрузку на сервере. Проверьте соединение и повторите отмену.";
        notify();
        pump();
        return;
      }
    }
    await forget(t);
    notify();
    pump();
  } else if (action === "skip" || action === "copy" || action === "skipAll") {
    if (action === "skipAll") skipAll = true;
    void finalize(t, action === "copy" ? "copy" : "skip");
  }
}
export function clearFinished() {
  queue = queue.filter(
    (t) => !["complete", "skipped", "cancelled"].includes(t.status),
  );
  notify();
}

export async function addDirectoryUploads(files: File[], target: string) {
  const created = new Set<string>();
  for (const file of files) {
    const relative = file.webkitRelativePath || file.name;
    const parts = relative.split("/").slice(0, -1);
    if (parts.some((p) => !p || p === "." || p === ".." || p.includes("\\")))
      throw new Error("Некорректное имя папки.");
    let parent = target;
    for (const name of parts) {
      const path = [parent, name].filter(Boolean).join("/");
      if (!created.has(path)) {
        const folders = await api<{ path: string }[]>(
          "/folders?folder=" + encodeURIComponent(parent),
        );
        if (!folders.some((f) => f.path === path))
          await api("/folders", { parent, name });
        created.add(path);
      }
      parent = path;
    }
    addUploads([file], parent);
  }
}
